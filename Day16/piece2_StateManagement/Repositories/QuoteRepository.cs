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

    // Query 1: SELECT DISTINCT Author FROM Quotes WHERE IsDeleted=0  (full table scan — no index on Author)
    // Query 2..N+1: SELECT Text FROM Quotes WHERE Author=@a AND IsDeleted=0  (full scan per author)
    public async Task<Dictionary<string, List<string>>> GetByAuthorSlowAsync(CancellationToken cancellationToken)
    {
        var authors = await _context.Quotes
            .Where(q => !q.IsDeleted)
            .Select(q => q.Author)
            .Distinct()
            .ToListAsync(cancellationToken);

        var result = new Dictionary<string, List<string>>(authors.Count);
        foreach (var author in authors)
        {
            var texts = await _context.Quotes
                .Where(q => q.Author == author && !q.IsDeleted)
                .Select(q => q.Text)
                .ToListAsync(cancellationToken);

            result[author] = texts;
        }
        return result;
    }

    // Single query: SELECT Author, Text FROM Quotes WHERE IsDeleted=0 ORDER BY Author
    // Eliminates N+1 — Author index turns per-author full scans into one covering range scan.
    public async Task<Dictionary<string, List<string>>> GetByAuthorFastAsync(CancellationToken cancellationToken)
    {
        var rows = await _context.Quotes
            .Where(q => !q.IsDeleted)
            .OrderBy(q => q.Author)
            .Select(q => new { q.Author, q.Text })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.Author)
            .ToDictionary(g => g.Key, g => g.Select(r => r.Text).ToList());
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
