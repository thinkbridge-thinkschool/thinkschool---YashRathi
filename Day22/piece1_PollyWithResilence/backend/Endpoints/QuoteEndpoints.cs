using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Caching.Memory;
using QuotesApi.BackgroundJobs;
using QuotesApi.Cache;
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

        // ── Hot read: paginated list via HybridCache (L1 + L2 + stampede protection) ──────
        // Cache key encodes every filter dimension so each unique query gets its own slot.
        // All list entries share TagLists so a single RemoveByTagAsync clears every page.
        group.MapGet("/", async (
            int page, int size,
            string? author,
            string? text,
            HybridCache hybridCache,
            CacheMetrics metrics,
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

            metrics.RecordRequest();
            var quotes = await hybridCache.GetOrCreateAsync(
                CacheKeys.QuotesList(page, size, author, text),
                async cancel =>
                {
                    metrics.RecordDbQuery();
                    logger.LogDebug("Cache MISS for quotes list page={Page}", page);
                    return await handler.HandleAsync(new GetQuotesQuery(page, size, author, text), cancel);
                },
                new HybridCacheEntryOptions { Expiration = TimeSpan.FromSeconds(30) },
                [CacheKeys.TagLists],
                cancellationToken);

            activity?.SetTag("quotes.count", quotes?.Count ?? 0);
            return Results.Ok(quotes);
        });

        // ── Hot read: single quote by ID ────────────────────────────────────────────────────
        // This is the primary hot-read target for the stampede protection demo.
        // All single-quote entries share TagIds so a bulk flush is possible in development.
        group.MapGet("/{id:int}", async (
            int id,
            HybridCache hybridCache,
            CacheMetrics metrics,
            GetQuoteByIdQueryHandler handler,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            logger.LogInformation("Fetching quote {QuoteId}", id);

            metrics.RecordRequest();
            var quote = await hybridCache.GetOrCreateAsync(
                CacheKeys.QuoteById(id),
                async cancel =>
                {
                    metrics.RecordDbQuery();
                    logger.LogDebug("Cache MISS for quote {QuoteId} — querying DB", id);
                    return await handler.HandleAsync(new GetQuoteByIdQuery(id), cancel);
                },
                new HybridCacheEntryOptions { Expiration = TimeSpan.FromMinutes(5) },
                [CacheKeys.TagIds],
                cancellationToken);

            if (quote is null)
            {
                logger.LogWarning("Quote {QuoteId} not found", id);
                return Results.NotFound();
            }
            return Results.Ok(quote);
        });

        // ── No-cache bypass: used as the load-test baseline to measure raw DB cost ─────────
        // Hit /no-cache/{id} for "before" numbers; hit /{id} for "after" numbers.
        group.MapGet("/no-cache/{id:int}", async (
            int id,
            GetQuoteByIdQueryHandler handler,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            logger.LogInformation("Fetching quote {QuoteId} (no-cache bypass)", id);
            var quote = await handler.HandleAsync(new GetQuoteByIdQuery(id), cancellationToken);
            return quote is null ? Results.NotFound() : Results.Ok(quote);
        });

        // ── N+1 intentional anti-pattern ────────────────────────────────────────────────────
        group.MapGet("/by-author", async (
            IQuoteRepository repository,
            CancellationToken cancellationToken) =>
        {
            var result = await repository.GetByAuthorSlowAsync(cancellationToken);
            return Results.Ok(result);
        });

        // ── Optimised: IMemoryCache replaced with HybridCache ────────────────────────────
        // Before: IMemoryCache — no stampede protection, L1 only, single process.
        // After:  HybridCache  — stampede protected, L1 + L2 Redis, cluster-aware.
        group.MapGet("/by-author-fast", async (
            IQuoteRepository repository,
            HybridCache hybridCache,
            CacheMetrics metrics,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            using var activity = Telemetry.Source.StartActivity("list-by-author-fast");

            metrics.RecordRequest();
            var result = await hybridCache.GetOrCreateAsync(
                CacheKeys.ByAuthor,
                async cancel =>
                {
                    metrics.RecordDbQuery();
                    logger.LogDebug("Cache MISS for by-author — querying DB");
                    return await repository.GetByAuthorFastAsync(cancel);
                },
                new HybridCacheEntryOptions { Expiration = TimeSpan.FromSeconds(30) },
                [CacheKeys.TagLists],
                cancellationToken);

            activity?.SetTag("author.count", result?.Count ?? 0);
            return Results.Ok(result);
        });

        // ── Cache statistics: hit rate + DB query count ──────────────────────────────────
        group.MapGet("/cache-stats", (CacheMetrics metrics) => Results.Ok(new
        {
            requests   = metrics.Requests,
            dbQueries  = metrics.DbQueries,
            hits       = metrics.Hits,
            hitRatePct = metrics.HitRatePct,
            dbLoadDrop = metrics.Requests == 0 ? "n/a"
                : $"{Math.Round((1.0 - (double)metrics.DbQueries / metrics.Requests) * 100, 1)}%"
        }));

        group.MapDelete("/cache-stats", (CacheMetrics metrics) =>
        {
            metrics.Reset();
            return Results.NoContent();
        });

        // ── Dev helper: flush all HybridCache entries by tag ────────────────────────────
        group.MapDelete("/cache", async (HybridCache hybridCache, CancellationToken cancellationToken) =>
        {
            await hybridCache.RemoveByTagAsync(CacheKeys.TagLists, cancellationToken);
            await hybridCache.RemoveByTagAsync(CacheKeys.TagIds, cancellationToken);
            return Results.NoContent();
        });

        // ── Stampede protection demo ─────────────────────────────────────────────────────
        // Fires `concurrency` concurrent cache-lookups for the SAME cold key using both
        // IMemoryCache and HybridCache, then returns how many times the factory (= DB call)
        // actually ran for each.
        //
        // IMemoryCache: multiple threads can all enter the factory before the first result
        // is committed, causing a thundering herd.
        //
        // HybridCache: exactly ONE factory call runs; every other waiter is coalesced and
        // receives the same result without touching the DB.
        group.MapGet("/stampede-demo", async (
            int concurrency,
            HybridCache hybridCache,
            IMemoryCache memCache,
            ILogger<Program> logger) =>
        {
            concurrency = Math.Clamp(concurrency, 2, 50);
            const string demoKey = "stampede:demo";
            const int delayMs = 200; // simulate a slow DB query

            // ── IMemoryCache ───────────────────────────────────────────────────────────
            memCache.Remove(demoKey + ":mem");
            var memCalls = 0;

            var memTasks = Enumerable.Range(0, concurrency).Select(_ => Task.Run(async () =>
                await memCache.GetOrCreateAsync(demoKey + ":mem", async entry =>
                {
                    Interlocked.Increment(ref memCalls);
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
                    await Task.Delay(delayMs);
                    return "ok";
                })
            )).ToArray();
            await Task.WhenAll(memTasks);
            memCache.Remove(demoKey + ":mem");

            // ── HybridCache ────────────────────────────────────────────────────────────
            await hybridCache.RemoveAsync(demoKey);
            var hybridCalls = 0;

            var hybridTasks = Enumerable.Range(0, concurrency).Select(_ => Task.Run(async () =>
                await hybridCache.GetOrCreateAsync<string>(demoKey, async cancel =>
                {
                    Interlocked.Increment(ref hybridCalls);
                    await Task.Delay(delayMs, cancel);
                    return "ok";
                })
            )).ToArray();
            await Task.WhenAll(hybridTasks);
            await hybridCache.RemoveAsync(demoKey);

            logger.LogInformation(
                "Stampede demo: concurrency={C} memCalls={M} hybridCalls={H}",
                concurrency, memCalls, hybridCalls);

            return Results.Ok(new
            {
                concurrency,
                factoryDelayMs = delayMs,
                memoryCache = new
                {
                    factoryCalls     = memCalls,
                    stampedeOccurred = memCalls > 1,
                    wastedDbQueries  = Math.Max(0, memCalls - 1)
                },
                hybridCache = new
                {
                    factoryCalls      = hybridCalls,
                    stampedeEliminated = hybridCalls == 1,
                    savedDbQueries    = Math.Max(0, memCalls - hybridCalls)
                },
                verdict = hybridCalls == 1
                    ? $"{concurrency} concurrent requests → 1 DB call. HybridCache coalesced {concurrency - 1} waiters. " +
                      $"IMemoryCache fired {memCalls} DB calls for the same load."
                    : $"Unexpected: HybridCache fired {hybridCalls} calls (expected 1). Run again — timing edge-case."
            });
        });

        // ── Same read shape as GET / but served via Dapper ────────────────────────────
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

        // ── EF vs Dapper benchmark ───────────────────────────────────────────────────
        group.MapGet("/bench", async (
            int page, int size, int iterations,
            GetQuotesQueryHandler efHandler,
            GetQuotesDapperQueryHandler dapperHandler,
            CancellationToken cancellationToken) =>
        {
            var query = new GetQuotesQuery(page, size);

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
                ef = new { totalMs = efSw.ElapsedMilliseconds, avgMicros = efAvgMicros },
                dapper = new { totalMs = dapperSw.ElapsedMilliseconds, avgMicros = dapperAvgMicros },
                speedupFactor = efAvgMicros > 0 ? Math.Round(efAvgMicros / dapperAvgMicros, 2) : 0
            });
        });

        // ── Write path: create quote + invalidate list caches ───────────────────────
        group.MapPost("/", async (
            CreateQuoteRequest request,
            ClaimsPrincipal user,
            HybridCache hybridCache,
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

            // Invalidate all paginated lists and the by-author summary.
            // The new quote must appear immediately on the next list request.
            await hybridCache.RemoveByTagAsync(CacheKeys.TagLists, cancellationToken);

            activity?.SetTag("quote.id", result.Value);
            logger.LogInformation("Quote {QuoteId} created by user {UserId}", result.Value, ownerId);

            await emailOutbox.EnqueueAsync(
                new EmailOutboxJob(
                    To: ownerId ?? "system",
                    Subject: $"Quote #{result.Value} added",
                    Body: $"\"{request.Text}\" — {request.Author}"),
                cancellationToken);

            return Results.Created($"/api/quotes/{result.Value}", new { id = result.Value });
        }).RequireAuthorization("can-edit-quotes");

        // ── Delete: invalidate specific key + all list caches ────────────────────────
        group.MapDelete("/{id:int}", async (
            int id,
            ClaimsPrincipal user,
            HybridCache hybridCache,
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

            // Evict the specific quote entry and all list snapshots that may reference it.
            await hybridCache.RemoveAsync(CacheKeys.QuoteById(id), cancellationToken);
            await hybridCache.RemoveByTagAsync(CacheKeys.TagLists, cancellationToken);

            logger.LogInformation("Quote {QuoteId} deleted by user {UserId}", id, userId);
            return Results.NoContent();
        }).RequireAuthorization();

        return app;
    }
}
