using QuotesApi.Models;

namespace QuotesApi.Outbox;

// Default publisher: logs the event payload.
// Swap this out for an Azure Service Bus or RabbitMQ publisher without touching the relay.
public sealed class LoggingMessagePublisher : IMessagePublisher
{
    private readonly ILogger<LoggingMessagePublisher> _logger;

    public LoggingMessagePublisher(ILogger<LoggingMessagePublisher> logger) =>
        _logger = logger;

    public Task PublishAsync(OutboxMessage message, CancellationToken ct)
    {
        _logger.LogInformation(
            "[MessageBus] Published {EventType} | id={MessageId} | payload={Payload}",
            message.EventType, message.Id, message.Payload);
        return Task.CompletedTask;
    }
}
