using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Repositories;

public class QuoteRepository : IQuoteRepository
{
    private readonly AppDbContext _context;

    public QuoteRepository(AppDbContext context) => _context = context;

    public async Task<List<Quote>> GetAllAsync(int page, int size, CancellationToken cancellationToken) =>
        await _context.Quotes
            .Where(q => !q.IsDeleted)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);

    // N+1 intentionally bad: fetches IDs first, then one SELECT per ID.
    // Each iteration fires a separate EF Core roundtrip — visible as N child spans in Jaeger.
    public async Task<List<Quote>> GetAllSlowAsync(int page, int size, CancellationToken cancellationToken)
    {
        var ids = await _context.Quotes
            .Where(q => !q.IsDeleted)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(q => q.Id)
            .ToListAsync(cancellationToken);

        var quotes = new List<Quote>(ids.Count);
        foreach (var id in ids)
        {
            var quote = await _context.Quotes
                .FirstOrDefaultAsync(q => q.Id == id, cancellationToken);
            if (quote is not null) quotes.Add(quote);
        }
        return quotes;
    }

    public async Task<Quote?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        await _context.Quotes
            .FirstOrDefaultAsync(q => q.Id == id && !q.IsDeleted, cancellationToken);

    public async Task<Quote> AddAsync(Quote quote, CancellationToken cancellationToken)
    {
        _context.Quotes.Add(quote);
        await _context.SaveChangesAsync(cancellationToken);
        return quote;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var quote = await _context.Quotes
            .FirstOrDefaultAsync(q => q.Id == id && !q.IsDeleted, cancellationToken);
        if (quote is null) return false;
        quote.SoftDelete();
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}
