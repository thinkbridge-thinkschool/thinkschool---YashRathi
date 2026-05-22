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
            CancellationToken cancellationToken) =>
        {
            var quotes = await repository.GetAllAsync(page, size, cancellationToken);
            return Results.Ok(quotes);
        });

        group.MapGet("/{id:int}", async (
            int id,
            IQuoteRepository repository,
            CancellationToken cancellationToken) =>
        {
            var quote = await repository.GetByIdAsync(id, cancellationToken);
            return quote is null ? Results.NotFound() : Results.Ok(quote);
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
            var result = Quote.Create(request.Author, request.Text, clock.UtcNow, ownerId);
            if (!result.IsSuccess)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["quote"] = [result.Error!.Message]
                });

            logger.LogInformation("Creating quote for author {Author}", request.Author);
            var created = await repository.AddAsync(result.Value!, cancellationToken);
            return Results.Created($"/api/quotes/{created.Id}", created);
        }).RequireAuthorization("can-edit-quotes");

        // Policy 2 — resource-based: caller must own the quote.
        // RequireAuthorization() enforces authentication; the ownership check
        // happens inside via IAuthorizationService so we have the resource at hand.
        group.MapDelete("/{id:int}", async (
            int id,
            ClaimsPrincipal user,
            IAuthorizationService authService,
            IQuoteRepository repository,
            CancellationToken cancellationToken) =>
        {
            var quote = await repository.GetByIdAsync(id, cancellationToken);
            if (quote is null) return Results.NotFound();

            var auth = await authService.AuthorizeAsync(user, quote, "can-delete-own-quote");
            if (!auth.Succeeded) return Results.Forbid();

            await repository.DeleteAsync(id, cancellationToken);
            return Results.NoContent();
        }).RequireAuthorization();

        return app;
    }
}
