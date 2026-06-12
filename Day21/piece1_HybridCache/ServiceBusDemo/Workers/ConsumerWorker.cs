using Azure.Messaging.ServiceBus;
using ServiceBusDemo.Handlers;
using ServiceBusDemo.Models;
using ServiceBusDemo.Services;

namespace ServiceBusDemo.Workers;

/// <summary>
/// Competing-consumer worker: one instance per subscription.
/// MaxConcurrentCalls=2 means two goroutine slots compete for the same subscription's messages.
/// Idempotency: checks ProcessedMessageStore before handling; marks processed only on success.
/// </summary>
public sealed class ConsumerWorker(
    ServiceBusClient client,
    ProcessedMessageStore store,
    IQuoteHandler handler,
    ILogger<ConsumerWorker> logger) : BackgroundService
{
    private const string TopicName = "quotes-topic";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = new ServiceBusProcessorOptions
        {
            MaxConcurrentCalls = 2,
            AutoCompleteMessages = false
        };

        await using var processor = client.CreateProcessor(TopicName, handler.SubscriptionName, options);
        processor.ProcessMessageAsync += OnMessage;
        processor.ProcessErrorAsync += OnError;

        logger.LogInformation("[{Sub}] Worker starting — MaxConcurrentCalls=2", handler.SubscriptionName);
        await processor.StartProcessingAsync(stoppingToken);

        try { await Task.Delay(Timeout.Infinite, stoppingToken); }
        catch (OperationCanceledException) { }

        await processor.StopProcessingAsync();
        logger.LogInformation("[{Sub}] Worker stopped", handler.SubscriptionName);
    }

    private async Task OnMessage(ProcessMessageEventArgs args)
    {
        var msgId = args.Message.MessageId;
        var idempotencyKey = $"{handler.SubscriptionName}:{msgId}";

        // ── Idempotency gate ──────────────────────────────────────────────────
        if (store.IsProcessed(idempotencyKey))
        {
            logger.LogWarning(
                "[{Sub}] IDEMPOTENCY-SKIP [{MessageId}] already processed — completing without re-handling",
                handler.SubscriptionName, msgId);
            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
            return;
        }

        // ── Handle ────────────────────────────────────────────────────────────
        try
        {
            var quote = args.Message.Body.ToObjectFromJson<QuoteMessage>()
                ?? throw new InvalidOperationException($"Failed to deserialize message [{msgId}]");
            await handler.HandleAsync(quote, msgId, args.CancellationToken);

            // Mark AFTER success so failed attempts are retryable
            store.MarkProcessed(idempotencyKey);
            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[{Sub}] HANDLER-ERROR [{MessageId}] delivery={Delivery} — abandoning (retry or DLQ)",
                handler.SubscriptionName, msgId, args.Message.DeliveryCount);

            // Abandon returns the message to the queue for retry.
            // After MaxDeliveryCount retries the broker moves it to the DLQ automatically.
            await args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken);
        }
    }

    private Task OnError(ProcessErrorEventArgs args)
    {
        logger.LogError(args.Exception,
            "[{Sub}] ServiceBus infrastructure error source={Source}",
            handler.SubscriptionName, args.ErrorSource);
        return Task.CompletedTask;
    }
}
