using Azure.Messaging.ServiceBus;
using ServiceBusDemo.Models;

namespace ServiceBusDemo.Publisher;

public sealed class TopicPublisher : IAsyncDisposable
{
    private readonly ServiceBusSender _sender;
    private readonly ILogger<TopicPublisher> _logger;

    public TopicPublisher(ServiceBusClient client, IConfiguration config, ILogger<TopicPublisher> logger)
    {
        var topicName = config["ServiceBus:TopicName"] ?? "quotes-topic";
        _sender = client.CreateSender(topicName);
        _logger = logger;
    }

    public async Task PublishAsync(QuoteMessage quote, CancellationToken ct = default)
    {
        var message = new ServiceBusMessage(BinaryData.FromObjectAsJson(quote))
        {
            MessageId = quote.Id,
            ContentType = "application/json",
            Subject = quote.IsPoisonous ? "poison" : "quote"
        };

        await _sender.SendMessageAsync(message, ct);
        _logger.LogInformation(
            "PUBLISH [{MessageId}] author={Author} poisonous={Poison}",
            quote.Id, quote.Author, quote.IsPoisonous);
    }

    public async ValueTask DisposeAsync() => await _sender.DisposeAsync();
}
