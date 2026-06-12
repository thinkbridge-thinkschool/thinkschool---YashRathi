using System.Threading.Channels;

namespace QuotesApi.BackgroundJobs;

// BackgroundService = IHostedService + a single abstract ExecuteAsync to override.
// This class wears two hats: it is both the channel writer (IEmailOutbox — used by
// request threads to enqueue work) and the channel reader (BackgroundService — the
// long-lived loop that drains and processes jobs off the request path).
//
// Registration pattern (see InfrastructureExtensions):
//   AddSingleton<EmailOutboxWorker>()
//   AddSingleton<IEmailOutbox>(sp => sp.GetRequiredService<EmailOutboxWorker>())
//   AddHostedService(sp => sp.GetRequiredService<EmailOutboxWorker>())
// One singleton, three roles: DI, outbox interface, hosted worker.
public class EmailOutboxWorker : BackgroundService, IEmailOutbox
{
    private readonly Channel<EmailOutboxJob> _channel;
    private readonly ILogger<EmailOutboxWorker> _logger;

    public EmailOutboxWorker(ILogger<EmailOutboxWorker> logger)
    {
        _logger = logger;
        // Bounded: at 100 pending jobs EnqueueAsync will await instead of
        // allocating unbounded memory. SingleReader avoids lock contention.
        _channel = Channel.CreateBounded<EmailOutboxJob>(
            new BoundedChannelOptions(capacity: 100)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true
            });
    }

    // IEmailOutbox — called from request threads; ValueTask avoids an allocation
    // when the channel has space (the common case).
    public ValueTask EnqueueAsync(EmailOutboxJob job, CancellationToken ct = default)
        => _channel.Writer.WriteAsync(job, ct);

    // Runs for the full lifetime of the host on a background thread.
    // stoppingToken is cancelled when IHost.StopAsync is called (SIGTERM, Ctrl+C,
    // or the graceful-shutdown timeout — default 5 s in ASP.NET Core).
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("EmailOutboxWorker started");

        // ReadAllAsync WITHOUT a cancellation token — the ONLY exit condition is
        // that the writer is completed (TryComplete in StopAsync) AND the channel
        // is empty. Passing stoppingToken here would abort the loop mid-drain as
        // soon as the host cancels the token, silently dropping buffered jobs.
        await foreach (var job in _channel.Reader.ReadAllAsync())
        {
            try
            {
                // stoppingToken is forwarded to individual jobs so long-running
                // I/O can be cancelled after shutdown; short jobs complete anyway.
                await ProcessAsync(job, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Log and keep the loop alive — one bad message must not kill
                // the worker for all subsequent messages.
                _logger.LogError(ex, "Failed to process email to {To}", job.To);
            }
        }

        _logger.LogInformation("EmailOutboxWorker drained and stopped");
    }

    // Graceful shutdown sequence:
    //   1. TryComplete() — seals the writer; items already in the channel remain readable.
    //   2. base.StopAsync() — cancels stoppingToken then awaits ExecuteAsync.
    // Because ReadAllAsync has no cancellation token, it keeps draining until the
    // channel is both complete AND empty before ExecuteAsync returns.
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _channel.Writer.TryComplete();
        await base.StopAsync(cancellationToken);
    }

    // Protected virtual so tests can subclass and observe calls without real I/O.
    protected virtual async Task ProcessAsync(EmailOutboxJob job, CancellationToken ct)
    {
        await Task.Delay(5, ct); // simulate network I/O (replace with real smtp/sendgrid)
        _logger.LogInformation("Email → {To} | {Subject}", job.To, job.Subject);
    }
}
