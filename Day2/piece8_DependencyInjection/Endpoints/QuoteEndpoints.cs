using QuotesApi.Abstractions;
using QuotesApi.Dtos;
using QuotesApi.Models;
using QuotesApi.Repositories;
namespace QuotesApi.Endpoints;
public static class QuoteEndpoints
{
    public static IEndpointRouteBuilder MapQuoteEndpoints(
        this IEndpointRouteBuilder app)
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
        group.MapPost("/", async (
            CreateQuoteRequest request,
            IQuoteRepository repository,
            ILogger<Program> logger,
            IClock clock,
            CancellationToken cancellationToken) =>
        {
            var result = Quote.Create(request.Author, request.Text, clock.UtcNow);
            if (!result.IsSuccess)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["quote"] = [result.Error!.Message]
                });

            logger.LogInformation("Creating quote for author {Author}", request.Author);
            var createdQuote = await repository.AddAsync(result.Value!, cancellationToken);
            return Results.Created($"/api/quotes/{createdQuote.Id}", createdQuote);
        });
        group.MapDelete("/{id:int}", async (
            int id,
            IQuoteRepository repository,
            CancellationToken cancellationToken) =>
        {
            var deleted = await repository.DeleteAsync(id, cancellationToken);
            return deleted ? Results.NoContent() : Results.NotFound();
        });
        return app;
    }
}