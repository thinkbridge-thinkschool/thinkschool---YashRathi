using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.ReadModels;

namespace QuotesApi.Queries;

public sealed class GetQuotesQueryHandler
{
    private readonly AppDbContext _context;

    public GetQuotesQueryHandler(AppDbContext context) => _context = context;

    // Projects directly to the screen shape — IsDeleted and OwnerId never leave the DB layer.
    // Author and Text filters are independent: both can be active at once (AND logic).
    public Task<List<QuoteListItem>> HandleAsync(GetQuotesQuery query, CancellationToken cancellationToken)
    {
        var q = _context.Quotes.Where(q => !q.IsDeleted);

        if (!string.IsNullOrWhiteSpace(query.Author))
            q = q.Where(x => EF.Functions.Like(x.Author, $"%{query.Author.Trim()}%"));

        if (!string.IsNullOrWhiteSpace(query.Text))
            q = q.Where(x => EF.Functions.Like(x.Text, $"%{query.Text.Trim()}%"));

        return q
            .OrderBy(x => x.Id)
            .Skip((query.Page - 1) * query.Size)
            .Take(query.Size)
            .Select(x => new QuoteListItem(x.Id, x.Author, x.Text, x.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}
