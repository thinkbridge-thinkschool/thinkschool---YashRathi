using QuotesApi.Models;

namespace QuotesApi.Outbox;

public interface IMessagePublisher
{
    Task PublishAsync(OutboxMessage message, CancellationToken ct);
}
