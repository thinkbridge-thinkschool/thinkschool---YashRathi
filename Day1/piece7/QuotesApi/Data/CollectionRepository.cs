using Microsoft.EntityFrameworkCore;
using QuotesApi.Models;

namespace QuotesApi.Data;

public class CollectionRepository : ICollectionRepository
{
    private readonly QuoteDbContext _db;

    public CollectionRepository(QuoteDbContext db) => _db = db;

    public Task<Collection?> GetByIdAsync(Guid id, CancellationToken ct) =>
        _db.Collections.FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task AddAsync(Collection collection, CancellationToken ct)
    {
        _db.Collections.Add(collection);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Collection collection, CancellationToken ct) =>
        await _db.SaveChangesAsync(ct);

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var collection = await _db.Collections.FindAsync(new object[] { id }, ct);
        if (collection is not null)
        {
            _db.Collections.Remove(collection);
            await _db.SaveChangesAsync(ct);
        }
    }
}
