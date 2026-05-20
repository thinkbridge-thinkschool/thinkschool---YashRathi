using QuotesApi.Models;

namespace QuotesApi.Repositories;

public interface IQuoteRepository
{
    Task<List<Quote>> GetPagedAsync(
        int page,
        int size,
        CancellationToken cancellationToken);

    Task<Quote?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken);

    Task<Quote> CreateAsync(
        Quote quote,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(
        int id,
        CancellationToken cancellationToken);
}