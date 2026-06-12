using QuotesApi.Models;

namespace QuotesApi.Repositories;

public interface IQuoteRepository
{
    Task<List<Quote>> GetAllAsync(int page, int size, CancellationToken cancellationToken);
    // N+1 variant intentionally introduced for observability demo — see piece29 exercise
    Task<List<Quote>> GetAllSlowAsync(int page, int size, CancellationToken cancellationToken);
    // Authors→quotes N+1: 1 DISTINCT Author query + 1 SELECT per author (no index on Author)
    Task<Dictionary<string, List<string>>> GetByAuthorSlowAsync(CancellationToken cancellationToken);
    // Single projection query + Author index: 1 SELECT Author,Text ORDER BY Author, grouped in memory
    Task<Dictionary<string, List<string>>> GetByAuthorFastAsync(CancellationToken cancellationToken);
    Task<Quote?> GetByIdAsync(int id, CancellationToken cancellationToken);
    Task<Quote> AddAsync(Quote quote, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken);
}
