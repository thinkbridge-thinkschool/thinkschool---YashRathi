using QuotesApi.Models;

namespace QuotesApi.Data;

public interface ICollectionRepository
{
    Task<Collection?> GetByIdAsync(Guid id, CancellationToken ct);
    Task AddAsync(Collection collection, CancellationToken ct);
    Task UpdateAsync(Collection collection, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
}
