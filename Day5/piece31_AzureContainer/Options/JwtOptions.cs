namespace QuotesApi.Options;

public record JwtOptions
{
    public string Key { get; init; } = string.Empty;
    public string Issuer { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;

    // Configuration binder parses ISO 8601 duration strings like "00:15:00".
    // Replaces the old int ExpiresInSeconds field.
    public TimeSpan AccessTokenLifetime { get; init; } = TimeSpan.FromMinutes(15);
}
