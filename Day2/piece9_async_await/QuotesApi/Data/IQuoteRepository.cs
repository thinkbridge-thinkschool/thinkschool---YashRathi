using QuotesApi.Models;

namespace QuotesApi.Data;

public interface IQuoteRepository
{
    Task<(IEnumerable<Quote> Quotes, int TotalCount)> GetPaginatedAsync(int page, int size, CancellationToken ct);
    Task<Quote?> GetByIdAsync(int id, CancellationToken ct);
    Task<Quote> AddAsync(CreateQuoteDto dto, CancellationToken ct);
    Task<bool> DeleteAsync(int id, CancellationToken ct);
}
