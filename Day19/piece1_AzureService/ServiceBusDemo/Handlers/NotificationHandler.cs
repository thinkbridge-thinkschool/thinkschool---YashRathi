using ServiceBusDemo.Models;

namespace ServiceBusDemo.Handlers;

/// <summary>
/// Subscription: sub-notifications
/// Sends quote to subscribers. Throws for poison messages, causing retries → DLQ.
/// </summary>
public sealed class NotificationHandler(ILogger<NotificationHandler> logger) : IQuoteHandler
{
    public string SubscriptionName => "sub-notifications";

    public Task HandleAsync(QuoteMessage quote, string messageId, CancellationToken ct)
    {
        if (quote.IsPoisonous)
            throw new InvalidOperationException(
                $"Poison payload detected in [{messageId}]: cannot notify subscribers");

        logger.LogInformation(
            "[NOTIFY] Delivered [{MessageId}] by {Author} to subscribers",
            messageId, quote.Author);
        return Task.CompletedTask;
    }
}
