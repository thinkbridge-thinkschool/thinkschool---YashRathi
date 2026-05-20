using System.ComponentModel.DataAnnotations;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Extensions;

public static class EndpointExtensions
{
    public static void MapQuoteEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/quotes");

        group.MapGet("/", async (int? page, int? size, IQuoteRepository repo, CancellationToken ct) =>
        {
            var p = page ?? 1;
            var s = size ?? 10;
            var (quotes, total) = await repo.GetPaginatedAsync(p, s, ct);
            return Results.Ok(new { Data = quotes, Total = total, Page = p, Size = s });
        });

        group.MapGet("/{id:int}", async (int id, IQuoteRepository repo, CancellationToken ct) =>
        {
            var quote = await repo.GetByIdAsync(id, ct);
            return quote is not null ? Results.Ok(quote) : Results.NotFound();
        });

        group.MapPost("/", async (CreateQuoteDto dto, IQuoteRepository repo, CancellationToken ct) =>
        {
            var validationResults = new List<ValidationResult>();
            var context = new ValidationContext(dto);
            if (!Validator.TryValidateObject(dto, context, validationResults, true))
            {
                var errors = validationResults
                    .GroupBy(e => e.MemberNames.FirstOrDefault() ?? string.Empty)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage ?? "Invalid").ToArray());
                return Results.ValidationProblem(errors);
            }

            var created = await repo.AddAsync(dto, ct);
            return Results.Created($"/api/quotes/{created.Id}", created);
        });

        group.MapDelete("/{id:int}", async (int id, IQuoteRepository repo, CancellationToken ct) =>
        {
            var success = await repo.DeleteAsync(id, ct);
            return success ? Results.NoContent() : Results.NotFound();
        });
    }
}
