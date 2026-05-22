using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using QuotesApi.Abstractions;
using QuotesApi.Dtos;
using QuotesApi.Models;
using QuotesApi.Repositories;

namespace QuotesApi.Endpoints;

public static class QuoteEndpoints
{
    public static IEndpointRouteBuilder MapQuoteEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/quotes");

        group.MapGet("/", async (
            int page, int size,
            IQuoteRepository repository,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            logger.LogInformation("Fetching quotes page={Page} size={Size}", page, size);
            var quotes = await repository.GetAllAsync(page, size, cancellationToken);
            return Results.Ok(quotes);
        });

        group.MapGet("/{id:int}", async (
            int id,
            IQuoteRepository repository,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            logger.LogInformation("Fetching quote {QuoteId}", id);
            var quote = await repository.GetByIdAsync(id, cancellationToken);
            if (quote is null)
            {
                logger.LogWarning("Quote {QuoteId} not found", id);
                return Results.NotFound();
            }
            return Results.Ok(quote);
        });

        // Policy 1 — claim-based: token must carry scope = quotes.write
        group.MapPost("/", async (
            CreateQuoteRequest request,
            ClaimsPrincipal user,
            IQuoteRepository repository,
            ILogger<Program> logger,
            IClock clock,
            CancellationToken cancellationToken) =>
        {
            var ownerId = user.FindFirstValue(JwtRegisteredClaimNames.Sub);

            using var activity = Telemetry.Source.StartActivity("create-quote");
            activity?.SetTag("user.id", ownerId);
            activity?.SetTag("quote.author", request.Author);

            // Line 1 of 5 — request received with owner and author context
            logger.LogInformation(
                "CreateQuote request received from user {UserId} for author {Author}",
                ownerId, request.Author);

            var result = Quote.Create(request.Author, request.Text, clock.UtcNow, ownerId);

            if (!result.IsSuccess)
            {
                // Line 2 of 5 — validation failure (structured error captured)
                logger.LogWarning(
                    "Quote validation failed for user {UserId}: {ValidationError}",
                    ownerId, result.Error!.Message);

                activity?.SetStatus(ActivityStatusCode.Error, result.Error!.Message);

                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["quote"] = [result.Error!.Message]
                });
            }

            // Line 3 of 5 — domain object passed validation
            logger.LogInformation(
                "Quote domain object created for author {Author} by user {UserId}",
                request.Author, ownerId);

            // Line 4 of 5 — persistence about to begin
            logger.LogInformation(
                "Persisting new quote to repository for user {UserId}", ownerId);

            var created = await repository.AddAsync(result.Value!, cancellationToken);
            activity?.SetTag("quote.id", created.Id);

            // Line 5 of 5 — success with the new resource identity
            logger.LogInformation(
                "Quote {QuoteId} created successfully for user {UserId}",
                created.Id, ownerId);

            return Results.Created($"/api/quotes/{created.Id}", created);
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
