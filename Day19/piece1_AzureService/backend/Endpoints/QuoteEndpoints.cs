using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Memory;
using QuotesApi.BackgroundJobs;
using QuotesApi.Commands;
using QuotesApi.Dtos;
using QuotesApi.Queries;
using QuotesApi.Repositories;
using QuotesApi.ReadModels;

namespace QuotesApi.Endpoints;

public static class QuoteEndpoints
{
    public static IEndpointRouteBuilder MapQuoteEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/quotes");

        // Read path: projects to QuoteListItem — IsDeleted/OwnerId never reach the caller.
        group.MapGet("/", async (
            int page, int size,
            string? author,
            string? text,
            GetQuotesQueryHandler handler,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            logger.LogInformation("Fetching quotes page={Page} size={Size} author={Author} text={Text}", page, size, author, text);

            using var activity = Telemetry.Source.StartActivity("list-quotes");
            activity?.SetTag("page", page);
            activity?.SetTag("size", size);
            activity?.SetTag("author.filter", author);
            activity?.SetTag("text.filter", text);

            var quotes = await handler.HandleAsync(new GetQuotesQuery(page, size, author, text), cancellationToken);

            activity?.SetTag("quotes.count", quotes.Count);
            return Results.Ok(quotes);
        });

        group.MapGet("/{id:int}", async (
            int id,
            GetQuoteByIdQueryHandler handler,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            logger.LogInformation("Fetching quote {QuoteId}", id);
            var quote = await handler.HandleAsync(new GetQuoteByIdQuery(id), cancellationToken);
            if (quote is null)
            {
                logger.LogWarning("Quote {QuoteId} not found", id);
                return Results.NotFound();
            }
            return Results.Ok(quote);
        });

        
        group.MapGet("/by-author", async (
            IQuoteRepository repository,
            CancellationToken cancellationToken) =>
        {
            var result = await repository.GetByAuthorSlowAsync(cancellationToken);
            return Results.Ok(result);
        });

        // Fixed: single projection query + composite index + 30 s memory cache.
        // Cache key "quotes:by-author" is shared across all VUs — only the first
        // request in each 30 s window hits the DB; the rest are served from RAM.
        group.MapGet("/by-author-fast", async (
            IQuoteRepository repository,
            IMemoryCache cache,
            CancellationToken cancellationToken) =>
        {
            using var activity = Telemetry.Source.StartActivity("list-by-author-fast");

            var result = await cache.GetOrCreateAsync("quotes:by-author", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
                return await repository.GetByAuthorFastAsync(cancellationToken);
            });

            activity?.SetTag("author.count", result?.Count ?? 0);
            activity?.SetTag("cache.hit", result is not null);
            return Results.Ok(result);
        });

        // Same read shape as GET /api/quotes but served via Dapper instead of EF Core.
        // SQL is sent verbatim; Dapper maps results via QuoteListItem's primary constructor.
        group.MapGet("/dapper", async (
            int page, int size,
            GetQuotesDapperQueryHandler handler,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            logger.LogInformation("Dapper: fetching quotes page={Page} size={Size}", page, size);
            var quotes = await handler.HandleAsync(new GetQuotesQuery(page, size), cancellationToken);
            return Results.Ok(quotes);
        });

        // Runs both handlers back-to-back (with warmup) and returns a timing comparison.
        // Exercise endpoint only — strip before production.
        group.MapGet("/bench", async (
            int page, int size, int iterations,
            GetQuotesQueryHandler efHandler,
            GetQuotesDapperQueryHandler dapperHandler,
            CancellationToken cancellationToken) =>
        {
            var query = new GetQuotesQuery(page, size);

            // Warmup: prime EF's internal query cache + SQLite's page cache.
            for (var i = 0; i < 5; i++)
            {
                await efHandler.HandleAsync(query, cancellationToken);
                await dapperHandler.HandleAsync(query, cancellationToken);
            }

            var efSw = Stopwatch.StartNew();
            for (var i = 0; i < iterations; i++)
                await efHandler.HandleAsync(query, cancellationToken);
            efSw.Stop();

            var dapperSw = Stopwatch.StartNew();
            for (var i = 0; i < iterations; i++)
                await dapperHandler.HandleAsync(query, cancellationToken);
            dapperSw.Stop();

            var efAvgMicros = Math.Round((double)efSw.ElapsedTicks / iterations / Stopwatch.Frequency * 1_000_000, 1);
            var dapperAvgMicros = Math.Round((double)dapperSw.ElapsedTicks / iterations / Stopwatch.Frequency * 1_000_000, 1);

            return Results.Ok(new
            {
                iterations,
                rowsPerPage = size,
                ef = new
                {
                    totalMs = efSw.ElapsedMilliseconds,
                    avgMicros = efAvgMicros,
                    sql = """
                        SELECT "q"."Id", "q"."Author", "q"."Text", "q"."CreatedAt"
                        FROM "Quotes" AS "q"
                        WHERE NOT ("q"."IsDeleted")
                        ORDER BY "q"."Id"
                        LIMIT @__p_1 OFFSET @__p_0
                        """
                },
                dapper = new
                {
                    totalMs = dapperSw.ElapsedMilliseconds,
                    avgMicros = dapperAvgMicros,
                    sql = """
                        SELECT Id, Author, Text, CreatedAt
                        FROM Quotes
                        WHERE IsDeleted = 0
                        ORDER BY Id
                        LIMIT @Size OFFSET @Offset
                        """
                },
                speedupFactor = efAvgMicros > 0 ? Math.Round(efAvgMicros / dapperAvgMicros, 2) : 0,
                rule = "Use Dapper on hot read paths where the query shape is fixed, the result " +
                       "is a DTO (no domain behaviour), and profiling shows EF's per-call overhead " +
                       "(expression-tree resolution, model-snapshot lookup, result-set shaping) is " +
                       "measurable. Keep EF for writes, migrations, dynamic/conditional queries, and " +
                       "any path that benefits from the change tracker or identity resolution. " +
                       "On a DTO projection with EF's compiled-query cache warm the delta is small " +
                       "(< 20 µs), so only reach for Dapper when you have profiling evidence — not " +
                       "as a default."
            });
        });

        // Write path: command handler owns validation + persistence.
        // Endpoint is reduced to auth, routing, and HTTP shape.
        group.MapPost("/", async (
            CreateQuoteRequest request,
            ClaimsPrincipal user,
            CreateQuoteCommandHandler handler,
            IEmailOutbox emailOutbox,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            var ownerId = user.FindFirstValue(JwtRegisteredClaimNames.Sub);

            using var activity = Telemetry.Source.StartActivity("create-quote");
            activity?.SetTag("user.id", ownerId);
            activity?.SetTag("quote.author", request.Author);

            logger.LogInformation(
                "CreateQuote request from user {UserId} for author {Author}",
                ownerId, request.Author);

            var result = await handler.HandleAsync(
                new CreateQuoteCommand(request.Author, request.Text, ownerId),
                cancellationToken);

            if (!result.IsSuccess)
            {
                activity?.SetStatus(ActivityStatusCode.Error, result.Error!.Message);
                logger.LogWarning(
                    "Quote validation failed for user {UserId}: {Error}",
                    ownerId, result.Error!.Message);
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["quote"] = [result.Error!.Message]
                });
            }

            activity?.SetTag("quote.id", result.Value);
            logger.LogInformation("Quote {QuoteId} created by user {UserId}", result.Value, ownerId);

            // Enqueue confirmation email off the request thread — the worker drains
            // this channel in the background; the caller gets 201 immediately.
            await emailOutbox.EnqueueAsync(
                new EmailOutboxJob(
                    To: ownerId ?? "system",
                    Subject: $"Quote #{result.Value} added",
                    Body: $"\"{request.Text}\" — {request.Author}"),
                cancellationToken);

            return Results.Created($"/api/quotes/{result.Value}", new { id = result.Value });
        }).RequireAuthorization("can-edit-quotes");

        // Policy 2 — resource-based: caller must own the quote.
        group.MapDelete("/{id:int}", async (
            int id,
            ClaimsPrincipal user,
            IAuthorizationService authService,
            IQuoteRepository repository,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            var userId = user.FindFirstValue(JwtRegisteredClaimNames.Sub);
            logger.LogInformation(
                "DeleteQuote request for quote {QuoteId} from user {UserId}", id, userId);

            var quote = await repository.GetByIdAsync(id, cancellationToken);
            if (quote is null)
            {
                logger.LogWarning("DeleteQuote: quote {QuoteId} not found", id);
                return Results.NotFound();
            }

            var auth = await authService.AuthorizeAsync(user, quote, "can-delete-own-quote");
            if (!auth.Succeeded)
            {
                logger.LogWarning(
                    "DeleteQuote authorization failed for user {UserId} on quote {QuoteId}",
                    userId, id);
                return Results.Forbid();
            }

            await repository.DeleteAsync(id, cancellationToken);
            logger.LogInformation("Quote {QuoteId} deleted by user {UserId}", id, userId);
            return Results.NoContent();
        }).RequireAuthorization();

        return app;
    }
}
