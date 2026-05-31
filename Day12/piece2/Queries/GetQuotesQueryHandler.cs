using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.ReadModels;

namespace QuotesApi.Queries;

public sealed class GetQuotesQueryHandler
{
    private readonly AppDbContext _context;

    public GetQuotesQueryHandler(AppDbContext context) => _context = context;

    // Projects directly to the screen shape — IsDeleted and OwnerId never leave the DB layer.
    public Task<List<QuoteListItem>> HandleAsync(GetQuotesQuery query, CancellationToken cancellationToken) =>
        _context.Quotes
            .Where(q => !q.IsDeleted)
            .OrderBy(q => q.Id)
            .Skip((query.Page - 1) * query.Size)
            .Take(query.Size)
            .Select(q => new QuoteListItem(q.Id, q.Author, q.Text, q.CreatedAt))
            .ToListAsync(cancellationToken);
}
