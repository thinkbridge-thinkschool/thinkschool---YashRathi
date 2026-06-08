using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.ReadModels;

namespace QuotesApi.Queries;

public sealed class GetQuoteByIdQueryHandler
{
    private readonly AppDbContext _context;

    public GetQuoteByIdQueryHandler(AppDbContext context) => _context = context;

    public Task<QuoteListItem?> HandleAsync(GetQuoteByIdQuery query, CancellationToken cancellationToken) =>
        _context.Quotes
            .Where(q => q.Id == query.Id && !q.IsDeleted)
            .Select(q => new QuoteListItem(q.Id, q.Author, q.Text, q.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);
}
