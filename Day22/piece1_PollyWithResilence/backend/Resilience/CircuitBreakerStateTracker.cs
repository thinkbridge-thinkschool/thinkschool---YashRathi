using Polly.CircuitBreaker;

namespace QuotesApi.Resilience;

/// <summary>
/// Singleton that exposes Polly's CircuitBreakerStateProvider for status queries
/// and maintains a human-readable event log for the prove/demo endpoints.
/// </summary>
public sealed class CircuitBreakerStateTracker
{
    private readonly List<string> _log = [];
    private readonly Lock _lock = new();

    // Passed into CircuitBreakerStrategyOptions.StateProvider so Polly updates it.
    public CircuitBreakerStateProvider StateProvider { get; } = new();

    public IReadOnlyList<string> Log
    {
        get { lock (_lock) return [.. _log]; }
    }

    public void LogEvent(string tag, string detail)
    {
        lock (_lock)
            _log.Add($"[{DateTimeOffset.UtcNow:HH:mm:ss.fff}] {tag,-14} {detail}");
    }

    public void ClearLog() { lock (_lock) _log.Clear(); }
}
