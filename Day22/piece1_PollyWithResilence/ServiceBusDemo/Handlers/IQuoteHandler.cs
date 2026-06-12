using ServiceBusDemo.Models;

namespace ServiceBusDemo.Handlers;

public interface IQuoteHandler
{
    string SubscriptionName { get; }
    Task HandleAsync(QuoteMessage quote, string messageId, CancellationToken ct);
}
