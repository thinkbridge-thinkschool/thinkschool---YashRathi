namespace QuotesApi.Models;

public class OutboxMessage
{
    public Guid Id { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public string Payload { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public int RetryCount { get; private set; }
    public string? LastError { get; private set; }

    private OutboxMessage() { }

    public static OutboxMessage Create(string eventType, string payload, DateTimeOffset occurredAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            EventType = eventType,
            Payload = payload,
            OccurredAt = occurredAt
        };

    public void MarkProcessed(DateTimeOffset processedAt)
    {
        ProcessedAt = processedAt;
        LastError = null;
    }

    public void RecordFailure(string error)
    {
        RetryCount++;
        LastError = error;
    }
}
