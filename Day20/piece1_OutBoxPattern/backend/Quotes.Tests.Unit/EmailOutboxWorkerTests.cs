using System.Threading.Channels;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using QuotesApi.BackgroundJobs;

namespace Quotes.Tests.Unit;

public class EmailOutboxWorkerTests
{
    // Subclass overrides ProcessAsync so tests can observe calls without real I/O.
    private sealed class TrackingWorker : EmailOutboxWorker
    {
        private int _count;
        private readonly TaskCompletionSource _firstProcessed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ProcessedCount => _count;
        public Task FirstProcessed => _firstProcessed.Task;

        public TrackingWorker() : base(NullLogger<EmailOutboxWorker>.Instance) { }

        protected override Task ProcessAsync(EmailOutboxJob job, CancellationToken ct)
        {
            Interlocked.Increment(ref _count);
            _firstProcessed.TrySetResult();
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task EnqueuedJob_IsProcessed_ByRunningWorker()
    {
        var worker = new TrackingWorker();
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        await worker.EnqueueAsync(new EmailOutboxJob("a@b.com", "Hi", "Body"));

        // Wait until the first job is processed (max 2 s to avoid hanging CI).
        await worker.FirstProcessed.WaitAsync(TimeSpan.FromSeconds(2));

        await worker.StopAsync(CancellationToken.None);

        worker.ProcessedCount.Should().Be(1);
    }

    [Fact]
    public async Task StopAsync_DrainsAllInFlightJobs_BeforeReturning()
    {
        // Verifies graceful shutdown: TryComplete() + base.StopAsync() lets
        // ReadAllAsync finish the remaining items before ExecuteAsync exits.
        var worker = new TrackingWorker();
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);

        // Enqueue a warmup item and wait for it to confirm the worker is
        // actually running before we queue the items we want to drain.
        // Without this gate the test is racy in a parallel-test environment
        // because ExecuteAsync may not be scheduled yet when StopAsync fires.
        await worker.EnqueueAsync(new EmailOutboxJob("warmup@x.com", "Warmup", "Body"));
        await worker.FirstProcessed.WaitAsync(TimeSpan.FromSeconds(2));

        for (var i = 0; i < 4; i++)
            await worker.EnqueueAsync(new EmailOutboxJob($"u{i}@x.com", "Sub", "Body"));

        await worker.StopAsync(CancellationToken.None);

        worker.ProcessedCount.Should().Be(5); // 1 warmup + 4 drain items
    }

    [Fact]
    public async Task EnqueueAsync_AfterStopAsync_ThrowsChannelClosedException()
    {
        // Verifies that TryComplete() seals the writer — new enqueues fail fast
        // rather than silently dropping jobs.
        var worker = new TrackingWorker();
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        await worker.StopAsync(CancellationToken.None);

        Func<Task> act = () => worker
            .EnqueueAsync(new EmailOutboxJob("x@y.com", "Late", "Body"))
            .AsTask();

        await act.Should().ThrowAsync<ChannelClosedException>();
    }
}
