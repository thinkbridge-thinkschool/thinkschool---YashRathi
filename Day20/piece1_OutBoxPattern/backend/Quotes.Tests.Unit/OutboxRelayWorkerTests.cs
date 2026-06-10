using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using QuotesApi.Abstractions;
using QuotesApi.Data;
using QuotesApi.Models;
using QuotesApi.Outbox;

namespace Quotes.Tests.Unit;

// ─────────────────────────────────────────────────────────────────────────────
// Fake publishers
// ─────────────────────────────────────────────────────────────────────────────

/// Crashes (throws) when asked to publish the specific message id.
file sealed class CrashingPublisher : IMessagePublisher
{
    private readonly Guid _crashOnId;
    public List<Guid> Published { get; } = [];

    public CrashingPublisher(Guid crashOnId) => _crashOnId = crashOnId;

    public Task PublishAsync(OutboxMessage message, CancellationToken ct)
    {
        if (message.Id == _crashOnId)
            throw new InvalidOperationException($"Simulated publish crash for {message.Id}");

        Published.Add(message.Id);
        return Task.CompletedTask;
    }
}

/// Records every message it receives without failing.
file sealed class RecordingPublisher : IMessagePublisher
{
    public List<Guid> Published { get; } = [];

    public Task PublishAsync(OutboxMessage message, CancellationToken ct)
    {
        Published.Add(message.Id);
        return Task.CompletedTask;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Test fixture
// ─────────────────────────────────────────────────────────────────────────────

// Wraps an explicit SqliteConnection that stays open for the test lifetime.
// Passing the DbConnection object (not a connection string) to EF Core means EF
// will NOT close it — the in-memory database persists for the duration of the test.
// The relay creates scoped AppDbContext instances from the same provider, so they
// all share this one physical connection and see each other's commits immediately.
file sealed class OutboxTestFixture : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;
    public AppDbContext Db { get; }

    private OutboxTestFixture(SqliteConnection conn, ServiceProvider provider, AppDbContext db)
    {
        _connection = conn;
        _provider = provider;
        Db = db;
    }

    public static OutboxTestFixture Create(IMessagePublisher publisher)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(connection));
        services.AddScoped<IMessagePublisher>(_ => publisher);

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        services.AddSingleton(clock);

        var provider = services.BuildServiceProvider();

        // Schema must be created while the connection is open.
        var db = provider.CreateScope().ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();

        return new OutboxTestFixture(connection, provider, db);
    }

    /// Build a relay that dispatches through the given publisher.
    public OutboxRelayWorker BuildRelay(IMessagePublisher publisher)
    {
        var services = new ServiceCollection();
        // Share the same open connection so relay sees the same rows as the test.
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(_connection));
        services.AddScoped<IMessagePublisher>(_ => publisher);

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        services.AddSingleton(clock);

        var p = services.BuildServiceProvider();
        return new OutboxRelayWorker(
            p.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<OutboxRelayWorker>.Instance);
    }

    /// Read outbox rows as-no-tracking so the relay's committed changes are visible.
    public Task<List<OutboxMessage>> ReadOutboxAsync() =>
        Db.OutboxMessages.AsNoTracking().ToListAsync();

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _connection.DisposeAsync();
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Tests
// ─────────────────────────────────────────────────────────────────────────────

public class OutboxRelayWorkerTests
{
    private static OutboxMessage MakeMessage(string eventType = "quote.created") =>
        OutboxMessage.Create(eventType, "{}", DateTimeOffset.UtcNow);

    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    public async Task RelayBatch_AllPending_PublishesAndMarksAllProcessed()
    {
        var publisher = new RecordingPublisher();
        await using var fx = OutboxTestFixture.Create(publisher);

        var msgs = new[] { MakeMessage(), MakeMessage(), MakeMessage() };
        fx.Db.OutboxMessages.AddRange(msgs);
        await fx.Db.SaveChangesAsync();

        var relay = fx.BuildRelay(publisher);
        await relay.RelayBatchAsync(CancellationToken.None);

        publisher.Published.Should().HaveCount(3);

        // AsNoTracking reads the relay's committed state (not fx.Db's change tracker cache).
        var fresh = await fx.ReadOutboxAsync();
        fresh.Should().AllSatisfy(m => m.ProcessedAt.Should().NotBeNull());
    }

    [Fact]
    public async Task RelayBatch_AlreadyProcessed_IsSkipped()
    {
        var publisher = new RecordingPublisher();
        await using var fx = OutboxTestFixture.Create(publisher);

        var done = MakeMessage();
        done.MarkProcessed(DateTimeOffset.UtcNow);
        var pending = MakeMessage();

        fx.Db.OutboxMessages.AddRange(done, pending);
        await fx.Db.SaveChangesAsync();

        var relay = fx.BuildRelay(publisher);
        await relay.RelayBatchAsync(CancellationToken.None);

        publisher.Published.Should().ContainSingle().Which.Should().Be(pending.Id);
    }

    // ── Crash scenario: proves at-least-once delivery ─────────────────────────
    //
    // SCENARIO TESTED
    //   The relay publishes msg1 successfully, then the publisher crashes when
    //   asked to publish msg2. Because the relay writes ProcessedAt only AFTER
    //   a successful publish(), msg2 stays in the outbox with ProcessedAt = null.
    //   On the next poll (Pass 2) the relay retries and publishes msg2.
    //
    // WHY NO MESSAGE IS LOST
    //   Commit order:  publish()  →  SaveChanges(ProcessedAt = now)
    //   Crash between: publish throws → SaveChanges is never called → ProcessedAt = null
    //   ⟹ next poll sees the row, retries. At-least-once delivery.
    //
    // WHY NO SPURIOUS DUPLICATE (idempotent consumer)
    //   Every OutboxMessage carries a stable Guid Id. If a crash happens AFTER
    //   publish() but BEFORE SaveChanges() commits, the relay publishes the same
    //   message twice. The consumer deduplicates using the Id as an idempotency key.

    [Fact]
    public async Task RelayBatch_PublisherCrashesOnMsg2_Msg2StaysPending()
    {
        var msg1 = MakeMessage();
        var msg2 = MakeMessage();

        // CrashingPublisher is deterministic: it crashes iff the message id matches.
        var crasher = new CrashingPublisher(crashOnId: msg2.Id);
        await using var fx = OutboxTestFixture.Create(crasher);

        fx.Db.OutboxMessages.AddRange(msg1, msg2);
        await fx.Db.SaveChangesAsync();

        var relay = fx.BuildRelay(crasher);
        await relay.RelayBatchAsync(CancellationToken.None);

        var rows = await fx.ReadOutboxAsync();

        rows.First(m => m.Id == msg1.Id).ProcessedAt
            .Should().NotBeNull("msg1 was published before the crash");

        var failedRow = rows.First(m => m.Id == msg2.Id);
        failedRow.ProcessedAt
            .Should().BeNull("crash before MarkProcessed → row stays pending");
        failedRow.RetryCount.Should().Be(1, "relay recorded the failure");
        failedRow.LastError.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task RelayBatch_AfterCrash_SecondPassPublishesRemainingMessage()
    {
        var msg1 = MakeMessage();
        var msg2 = MakeMessage();

        var crasher = new CrashingPublisher(crashOnId: msg2.Id);
        await using var fx = OutboxTestFixture.Create(crasher);

        fx.Db.OutboxMessages.AddRange(msg1, msg2);
        await fx.Db.SaveChangesAsync();

        // Pass 1: msg1 published OK, msg2 publish throws.
        var relay1 = fx.BuildRelay(crasher);
        await relay1.RelayBatchAsync(CancellationToken.None);

        // Pass 2: non-crashing relay on the SAME database (shared SqliteConnection).
        var retryPublisher = new RecordingPublisher();
        var relay2 = fx.BuildRelay(retryPublisher);
        await relay2.RelayBatchAsync(CancellationToken.None);

        // msg2 must be published in pass 2 — no message is permanently lost.
        retryPublisher.Published.Should().Contain(msg2.Id,
            "relay retries any row where ProcessedAt is still null");

        // msg1 must NOT be re-published — relay skips already-processed rows.
        retryPublisher.Published.Should().NotContain(msg1.Id,
            "msg1 was marked processed in pass 1 and must not be published again");

        // After both passes all rows are processed — no outbox backlog.
        var rows = await fx.ReadOutboxAsync();
        rows.Should().AllSatisfy(m => m.ProcessedAt.Should().NotBeNull());
    }

    // ── Idempotency: the message Id is the deduplication key ─────────────────

    [Fact]
    public void OutboxMessage_Id_IsStableAcrossRetries()
    {
        // The same Guid is preserved on every retry so consumers can deduplicate.
        var msg = MakeMessage();
        var id = msg.Id;

        msg.RecordFailure("first attempt failed");
        msg.RecordFailure("second attempt failed");

        msg.Id.Should().Be(id, "Id must never change — it is the idempotency key");
    }

    // ── Atomicity: domain row + outbox row commit in one EF transaction ───────

    [Fact]
    public async Task CreateQuote_InOneTransaction_WritesQuoteAndOutboxRow()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(connection));

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(new DateTimeOffset(2026, 6, 10, 12, 0, 0, TimeSpan.Zero));
        services.AddSingleton(clock);

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();

        var handler = new QuotesApi.Commands.CreateQuoteCommandHandler(db, clock);
        var result = await handler.HandleAsync(
            new QuotesApi.Commands.CreateQuoteCommand("Seneca", "Dum differtur vita transcurrit.", null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        // Both rows must be present — committed atomically in the same transaction.
        db.Quotes.Should().ContainSingle(q => q.Author == "Seneca");
        db.OutboxMessages.Should().ContainSingle(m => m.EventType == "quote.created");
        db.OutboxMessages.First().ProcessedAt.Should().BeNull(
            "relay has not run yet — message is pending publication");

        await connection.DisposeAsync();
    }
}
