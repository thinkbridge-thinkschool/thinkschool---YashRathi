namespace QuotesApi.Services;

/// <summary>
/// In-memory toggle that makes the /api/stub/service endpoint return HTTP 500.
/// Used by the resilience prove scenario to trigger circuit-breaker behaviour.
/// </summary>
public sealed class FaultSwitch
{
    private int _on; // 0 = healthy, 1 = faulting

    public bool IsOn => _on == 1;

    public void Enable()  => Interlocked.Exchange(ref _on, 1);
    public void Disable() => Interlocked.Exchange(ref _on, 0);
}
