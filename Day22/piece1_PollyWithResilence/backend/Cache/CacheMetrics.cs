namespace QuotesApi.Cache;

/// <summary>
/// Thread-safe counters for measuring HybridCache effectiveness.
/// RecordRequest() is called on every endpoint hit.
/// RecordDbQuery() is called only when the HybridCache factory executes (= cache miss → DB hit).
/// Hits = Requests − DbQueries.
/// </summary>
public sealed class CacheMetrics
{
    private long _requests;
    private long _dbQueries;

    public long Requests => Interlocked.Read(ref _requests);
    public long DbQueries => Interlocked.Read(ref _dbQueries);
    public long Hits => Math.Max(0, Requests - DbQueries);
    public double HitRatePct => Requests == 0 ? 0 : Math.Round((double)Hits / Requests * 100, 1);

    public void RecordRequest() => Interlocked.Increment(ref _requests);
    public void RecordDbQuery() => Interlocked.Increment(ref _dbQueries);

    public void Reset()
    {
        Interlocked.Exchange(ref _requests, 0);
        Interlocked.Exchange(ref _dbQueries, 0);
    }
}
