namespace QuotesApi.Models;

public class RefreshToken
{
    public int Id { get; set; }

    /// SHA-256 hex of the raw token sent to the client
    public string TokenHash { get; set; } = string.Empty;

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public DateTimeOffset ExpiresAt { get; set; }

    /// Set when this token is consumed or forcibly revoked
    public DateTimeOffset? RevokedAt { get; set; }

    /// Hash of the token that replaced this one (rotation audit trail)
    public string? ReplacedByToken { get; set; }

    /// All tokens minted from the same login share a FamilyId.
    /// Reuse detection revokes every token in the family.
    public string FamilyId { get; set; } = string.Empty;

    public bool IsRevoked => RevokedAt.HasValue;
    public bool IsExpired(DateTimeOffset now) => now > ExpiresAt;
    public bool IsActive(DateTimeOffset now) => !IsRevoked && !IsExpired(now);
}
