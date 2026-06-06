using QuotesApi.Models;

namespace QuotesApi.Repositories;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken ct);
    Task AddAsync(RefreshToken token, CancellationToken ct);
    Task RevokeFamilyAsync(string familyId, DateTimeOffset revokedAt, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
