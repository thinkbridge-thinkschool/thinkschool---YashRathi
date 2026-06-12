using Microsoft.EntityFrameworkCore;
using QuotesApi.Abstractions;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Outbox;

// Polls the OutboxMessages table every PollingInterval for rows where ProcessedAt IS NULL.
// For each pending message: publish → mark ProcessedAt = now → save.
//
// Crash safety (at-least-once):
//   - Crash BEFORE publish  → ProcessedAt stays null → next poll retries. No loss.
//   - Crash AFTER publish but BEFORE UPDATE → ProcessedAt stays null → next poll re-publishes.
//     Consumer must deduplicate by OutboxMessage.Id (idempotency key).
//   - Crash AFTER UPDATE committed → message is already marked processed. No duplicate.
//
// Each publish+update is its own DB write — if publish throws, RetryCount and LastError
// are recorded but the row stays pending so the relay picks it up again on the next tick.
public sealed class OutboxRelayWorker : BackgroundService
{
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(5);
    private const int BatchSize = 50;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxRelayWorker> _logger;

    public OutboxRelayWorker(IServiceScopeFactory scopeFactory, ILogger<OutboxRelayWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OutboxRelayWorker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RelayBatchAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "OutboxRelayWorker: unexpected error during relay batch");
            }

            await Task.Delay(PollingInterval, stoppingToken).ConfigureAwait(false);
        }

        _logger.LogInformation("OutboxRelayWorker stopped");
    }

    // Public so tests can call it directly to drive the relay without waiting for the timer.
    public async Task RelayBatchAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IMessagePublisher>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var pending = await db.OutboxMessages
            .Where(m => m.ProcessedAt == null)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (pending.Count == 0) return;

        _logger.LogInformation("OutboxRelayWorker: relaying {Count} pending message(s)", pending.Count);

        foreach (var message in pending)
        {
            try
            {
                await publisher.PublishAsync(message, ct);

                // Mark processed only AFTER successful publish — single row update.
                // If the process crashes here, ProcessedAt stays null and the relay retries.
                message.MarkProcessed(clock.UtcNow);
                await db.SaveChangesAsync(ct);

                _logger.LogInformation(
                    "OutboxRelayWorker: processed {EventType} id={MessageId}",
                    message.EventType, message.Id);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                message.RecordFailure(ex.Message);
                await db.SaveChangesAsync(ct);

                _logger.LogWarning(
                    ex,
                    "OutboxRelayWorker: publish failed for {EventType} id={MessageId} retry={Retry}",
                    message.EventType, message.Id, message.RetryCount);
            }
        }
    }
}
