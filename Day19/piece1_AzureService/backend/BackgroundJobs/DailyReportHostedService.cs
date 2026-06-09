namespace QuotesApi.BackgroundJobs;

// ── IHostedService directly (contrast with BackgroundService) ──────────────────
//
// BackgroundService is a convenience wrapper around IHostedService that adds the
// ExecuteAsync template method. Use IHostedService directly when:
//   • your work is timer-driven (not queue-driven), or
//   • you need precise control over the start/stop lifecycle that the wrapper
//     doesn't expose (e.g., you want StopAsync to flush state *before* the loop
//     is cancelled rather than after).
//
// ── When to choose Hangfire over a hosted service ─────────────────────────────
//
// Use Hangfire when jobs must survive a process restart (persisted queue),
// need automatic retries with exponential back-off, require a dashboard/audit
// trail, or are scheduled via a cron expression that must fire exactly once
// across multiple scaled-out instances. Use a hosted service when the work is
// purely in-process (no persistence needed) and the loss of pending jobs on
// restart is acceptable.
public sealed class DailyReportHostedService : IHostedService, IDisposable
{
    private readonly ILogger<DailyReportHostedService> _logger;
    private Timer? _timer;

    public DailyReportHostedService(ILogger<DailyReportHostedService> logger)
        => _logger = logger;

    // StartAsync must return quickly — long-running work goes in a background Task.
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("DailyReportHostedService starting");
        _timer = new Timer(Fire, null, TimeSpan.Zero, TimeSpan.FromHours(24));
        return Task.CompletedTask;
    }

    // StopAsync receives the shutdown cancellation token; stop the timer so it
    // does not fire again while the host is shutting down.
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    private void Fire(object? _)
        => _logger.LogInformation("[DailyReport] Generating report at {Time}", DateTimeOffset.UtcNow);

    public void Dispose() => _timer?.Dispose();
}
