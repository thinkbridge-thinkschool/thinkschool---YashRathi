using ServiceBusDemo.Models;

namespace ServiceBusDemo.Handlers;

/// <summary>
/// Subscription: sub-analytics
/// Processes every message, including poison ones (analytics tracks all events).
/// </summary>
public sealed class AnalyticsHandler(ILogger<AnalyticsHandler> logger) : IQuoteHandler
{
    public string SubscriptionName => "sub-analytics";

    public Task HandleAsync(QuoteMessage quote, string messageId, CancellationToken ct)
    {
        logger.LogInformation(
            "[ANALYTICS] Tracked [{MessageId}] author={Author} chars={Len} poisonous={Poison}",
            messageId, quote.Author, quote.Text.Length, quote.IsPoisonous);
        return Task.CompletedTask;
    }
}
