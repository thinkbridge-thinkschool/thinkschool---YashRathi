using Microsoft.EntityFrameworkCore;
using QuotesApi.Models;

namespace QuotesApi.Data;

public class QuoteRepository : IQuoteRepository
{
    private readonly QuoteDbContext _dbContext;
    private readonly ILogger<QuoteRepository> _logger;

    public QuoteRepository(QuoteDbContext dbContext, ILogger<QuoteRepository> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<(IEnumerable<Quote> Quotes, int TotalCount)> GetPaginatedAsync(int page, int size, CancellationToken ct)
    {
        _logger.LogInformation("Fetching quotes page {Page} with size {Size}", page, size);
        var query = _dbContext.Quotes.AsNoTracking();
        var total = await query.CountAsync(ct);
        var quotes = await query.Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return (quotes, total);
    }

    public async Task<Quote?> GetByIdAsync(int id, CancellationToken ct)
    {
        _logger.LogInformation("Fetching quote {Id}", id);
        return await _dbContext.Quotes.FindAsync(new object[] { id }, ct);
    }

    public async Task<Quote> AddAsync(CreateQuoteDto dto, CancellationToken ct)
    {
        _logger.LogInformation("Adding new quote by {Author}", dto.Author);
        var quote = new Quote { Author = dto.Author, Text = dto.Text };
        _dbContext.Quotes.Add(quote);
        await _dbContext.SaveChangesAsync(ct);
        return quote;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct)
    {
        _logger.LogInformation("Deleting quote {Id}", id);
        var quote = await _dbContext.Quotes.FindAsync(new object[] { id }, ct);
        if (quote is null) return false;

        _dbContext.Quotes.Remove(quote);
        await _dbContext.SaveChangesAsync(ct);
        return true;
    }
}
