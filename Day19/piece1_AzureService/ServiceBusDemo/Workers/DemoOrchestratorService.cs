using Azure.Messaging.ServiceBus;
using ServiceBusDemo.Models;
using ServiceBusDemo.Publisher;

namespace ServiceBusDemo.Workers;

/// <summary>
/// Runs the demo scenario:
///   Phase 1 — publish 3 normal quotes
///   Phase 2 — publish a duplicate (same MessageId as quote-001) → idempotency skip
///   Phase 3 — publish a poison message → retries exhaust → lands in DLQ
///   Phase 4 — probe the DLQ and print proof
/// </summary>
public sealed class DemoOrchestratorService(
    TopicPublisher publisher,
    ServiceBusClient client,
    IHostApplicationLifetime lifetime,
    ILogger<DemoOrchestratorService> logger) : BackgroundService
{
    private const string TopicName = "quotes-topic";
    private const string PoisonSubscription = "sub-notifications";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Give ConsumerWorkers time to connect their processors
        logger.LogInformation("═══════════════════════════════════════════════════════");
        logger.LogInformation("  Azure Service Bus Demo: Topics + DLQ + Idempotency  ");
        logger.LogInformation("═══════════════════════════════════════════════════════");
        logger.LogInformation("Waiting 4s for consumer workers to connect...");
        await Task.Delay(TimeSpan.FromSeconds(4), stoppingToken);

        // ── Phase 1: Publish 3 normal quotes ─────────────────────────────────
        logger.LogInformation("");
        logger.LogInformation("┌─ PHASE 1: Publishing 3 normal quotes ─────────────┐");
        var msg1 = new QuoteMessage("quote-001", "Marcus Aurelius",
            "You have power over your mind, not outside events.");
        var msg2 = new QuoteMessage("quote-002", "Albert Einstein",
            "Imagination is more important than knowledge.");
        var msg3 = new QuoteMessage("quote-003", "Maya Angelou",
            "Nothing will work unless you do.");

        await publisher.PublishAsync(msg1, stoppingToken);
        await publisher.PublishAsync(msg2, stoppingToken);
        await publisher.PublishAsync(msg3, stoppingToken);
        logger.LogInformation("└─ 3 messages published ────────────────────────────┘");

        // Allow workers to consume them before the duplicate arrives
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);

        // ── Phase 2: Publish duplicate (same MessageId = "quote-001") ────────
        logger.LogInformation("");
        logger.LogInformation("┌─ PHASE 2: Publishing DUPLICATE of quote-001 ───────┐");
        logger.LogInformation("  → MessageId stays 'quote-001'; ProcessedMessageStore");
        logger.LogInformation("    will detect it and skip without re-handling");
        var duplicate = msg1 with { Text = "INJECTED DUPLICATE — idempotency must skip this" };
        await publisher.PublishAsync(duplicate, stoppingToken);
        logger.LogInformation("└─ duplicate sent ──────────────────────────────────┘");

        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);

        // ── Phase 3: Publish poison message ──────────────────────────────────
        logger.LogInformation("");
        logger.LogInformation("┌─ PHASE 3: Publishing POISON message ───────────────┐");
        logger.LogInformation("  → NotificationHandler will throw on every delivery");
        logger.LogInformation("    After MaxDeliveryCount=3 attempts → DLQ");
        var poison = new QuoteMessage(
            "poison-007",
            "Evil Corp",
            "This message is deliberately malformed and will never succeed.",
            IsPoisonous: true);
        await publisher.PublishAsync(poison, stoppingToken);
        logger.LogInformation("└─ poison sent ─────────────────────────────────────┘");

        // Wait for retries to exhaust: 3 deliveries with fast abandons + margin
        logger.LogInformation("");
        logger.LogInformation("Waiting 30s for sub-notifications to exhaust retries and DLQ the poison...");
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        // ── Phase 4: DLQ Probe ────────────────────────────────────────────────
        logger.LogInformation("");
        logger.LogInformation("┌─ PHASE 4: DLQ Probe on {Sub} ──────────────┐", PoisonSubscription);
        await ProbeDlqAsync(stoppingToken);
        logger.LogInformation("└───────────────────────────────────────────────────┘");

        logger.LogInformation("");
        logger.LogInformation("═══════════════════════════════════════════════════════");
        logger.LogInformation("  Demo complete.                                       ");
        logger.LogInformation("═══════════════════════════════════════════════════════");
        lifetime.StopApplication();
    }

    private async Task ProbeDlqAsync(CancellationToken ct)
    {
        await using var receiver = client.CreateReceiver(
            TopicName,
            PoisonSubscription,
            new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter });

        var dlqMessages = await receiver.ReceiveMessagesAsync(
            maxMessages: 10,
            maxWaitTime: TimeSpan.FromSeconds(5),
            cancellationToken: ct);

        if (dlqMessages.Count == 0)
        {
            logger.LogWarning("[DLQ] No messages found — retries may not have exhausted yet.");
            return;
        }

        foreach (var msg in dlqMessages)
        {
            logger.LogInformation(
                "[DLQ] ★ DEAD-LETTERED  MessageId={MessageId}",
                msg.MessageId);
            logger.LogInformation(
                "[DLQ]   DeadLetterReason      = {Reason}",
                msg.DeadLetterReason);
            logger.LogInformation(
                "[DLQ]   DeadLetterDescription = {Desc}",
                msg.DeadLetterErrorDescription);
            logger.LogInformation(
                "[DLQ]   DeliveryCount         = {Count}",
                msg.DeliveryCount);

            var quote = msg.Body.ToObjectFromJson<QuoteMessage>();
            if (quote is not null)
            {
                logger.LogInformation(
                    "[DLQ]   Payload               = Author={Author} IsPoisonous={Poison}",
                    quote.Author, quote.IsPoisonous);
                logger.LogInformation(
                    "[DLQ]   Text                  = \"{Text}\"",
                    quote.Text[..Math.Min(60, quote.Text.Length)]);
            }

            // Abandon so the message stays in the DLQ for manual inspection
            await receiver.AbandonMessageAsync(msg, cancellationToken: ct);
        }

        logger.LogInformation(
            "[DLQ] PROOF CAPTURED — {Count} message(s) dead-lettered on {Sub}",
            dlqMessages.Count, PoisonSubscription);
    }
}
