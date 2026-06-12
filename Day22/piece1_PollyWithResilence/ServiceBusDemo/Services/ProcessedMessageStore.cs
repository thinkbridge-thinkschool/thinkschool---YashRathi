namespace ServiceBusDemo.Services;

/// <summary>
/// In-memory idempotency store: tracks successfully processed message IDs per subscription.
/// Key format: "{subscriptionName}:{messageId}"
/// </summary>
public sealed class ProcessedMessageStore
{
    private readonly HashSet<string> _processed = [];
    private readonly Lock _lock = new();

    public bool IsProcessed(string key)
    {
        lock (_lock) return _processed.Contains(key);
    }

    public void MarkProcessed(string key)
    {
        lock (_lock) _processed.Add(key);
    }
}
