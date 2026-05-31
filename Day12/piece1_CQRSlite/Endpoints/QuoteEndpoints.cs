using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Memory;
using QuotesApi.Commands;
using QuotesApi.Dtos;
using QuotesApi.Queries;
using QuotesApi.Repositories;

namespace QuotesApi.Endpoints;

public static class QuoteEndpoints
{
    public static IEndpointRouteBuilder MapQuoteEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/quotes");

        // Read path: projects to QuoteListItem — IsDeleted/OwnerId never reach the caller.
        group.MapGet("/", async (
            int page, int size,
            GetQuotesQueryHandler handler,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            logger.LogInformation("Fetching quotes page={Page} size={Size}", page, size);

            using var activity = Telemetry.Source.StartActivity("list-quotes");
            activity?.SetTag("page", page);
            activity?.SetTag("size", size);

            var quotes = await handler.HandleAsync(new GetQuotesQuery(page, size), cancellationToken);

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

        // Deliberately slow: 1 SELECT DISTINCT Author + 1 SELECT per author (N+1, no index on Author).
        // Used to demonstrate performance profiling with k6 + EF Core SQL logs + SQLite EXPLAIN.
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

        // Write path: command handler owns validation + persistence.
        // Endpoint is reduced to auth, routing, and HTTP shape.
        group.MapPost("/", async (
            CreateQuoteRequest request,
            ClaimsPrincipal user,
            CreateQuoteCommandHandler handler,
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
