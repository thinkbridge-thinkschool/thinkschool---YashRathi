using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Extensions;

public static class CollectionEndpoints
{
    public static void MapCollectionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/collections");

        group.MapPost("/", async (CreateCollectionRequest req, ICollectionRepository repo, CancellationToken ct) =>
        {
            Collection collection;
            try
            {
                collection = Collection.Create(req.Name, req.OwnerId);
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: 400, title: "Validation error");
            }

            await repo.AddAsync(collection, ct);
            return Results.Created($"/api/collections/{collection.Id}", collection);
        });

        group.MapGet("/{id:guid}", async (Guid id, ICollectionRepository repo, CancellationToken ct) =>
        {
            var collection = await repo.GetByIdAsync(id, ct);
            return collection is not null ? Results.Ok(collection) : Results.NotFound();
        });

        group.MapPost("/{id:guid}/items", async (Guid id, AddItemRequest req, ICollectionRepository repo, CancellationToken ct) =>
        {
            var collection = await repo.GetByIdAsync(id, ct);
            if (collection is null) return Results.NotFound();

            try
            {
                collection.AddItem(req.QuoteId);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: 400, title: "Invariant violated");
            }

            await repo.UpdateAsync(collection, ct);
            return Results.Ok(collection);
        });

        group.MapDelete("/{id:guid}/items/{quoteId:int}", async (Guid id, int quoteId, ICollectionRepository repo, CancellationToken ct) =>
        {
            var collection = await repo.GetByIdAsync(id, ct);
            if (collection is null) return Results.NotFound();

            try
            {
                collection.RemoveItem(quoteId);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: 400, title: "Invariant violated");
            }

            await repo.UpdateAsync(collection, ct);
            return Results.NoContent();
        });
    }
}

public record CreateCollectionRequest(string Name, string OwnerId);
public record AddItemRequest(int QuoteId);
