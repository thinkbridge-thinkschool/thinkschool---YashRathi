namespace QuotesApi.Services;

/// <summary>
/// Typed HttpClient for a downstream "external quotes" service.
/// Every method is a GET (idempotent), so the Polly retry-with-backoff
/// strategy is safe for all calls made through this client.
/// </summary>
public sealed class ExternalQuoteClient(HttpClient http, ILogger<ExternalQuoteClient> logger)
{
    public async Task<(bool Ok, string Body)> GetAsync(CancellationToken ct = default)
    {
        try
        {
            logger.LogDebug("ExternalQuoteClient → GET /api/stub/service");
            var response = await http.GetAsync("/api/stub/service", ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            response.EnsureSuccessStatusCode();
            return (true, body);
        }
        catch (Exception ex)
        {
            // Includes: HttpRequestException (500), TimeoutRejectedException,
            // BrokenCircuitException (open circuit), RateLimiterRejectedException (bulkhead full).
            logger.LogDebug("ExternalQuoteClient ✗ {Type}: {Msg}", ex.GetType().Name, ex.Message);
            return (false, $"{ex.GetType().Name}: {ex.Message}");
        }
    }
}
