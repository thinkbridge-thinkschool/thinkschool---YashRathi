using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Data;
using QuotesApi.Services;
using Xunit;

namespace QuotesApi.Tests;

/// <summary>
/// Verifies that the Polly resilience pipeline wired to the "external-quotes"
/// HttpClient retries on transient (5xx) failures and eventually succeeds.
///
/// Handler chain under test:
///   ResiliencePipeline (retry + CB + timeout) → TransientFailureHandler (stub)
///
/// TransientFailureHandler returns 503 for the first <c>failCount</c> calls,
/// then 200 with an empty JSON array. The test asserts that all three calls
/// were made (1 initial + 2 retries) and that the final result is a success.
/// </summary>
public class ResilienceTests : IAsyncLifetime
{
    private readonly ResilienceFixture _fixture = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    [Fact]
    public async Task GetTagsAsync_RetriesOnTransient503_AndSucceedsOnThirdAttempt()
    {
        // The stub fails twice (503) then returns 200 — matching 3 total calls
        // (1 initial + 2 retries) as configured in the resilience handler.
        var service = _fixture.Services.GetRequiredService<IExternalQuoteService>();

        var tags = await service.GetTagsAsync(quoteId: 1);

        tags.Should().NotBeNull("successful 200 response deserializes to an empty array");
        _fixture.Handler.CallCount.Should().Be(3,
            "Polly retried twice after the initial failure, making 3 total attempts");
    }

    [Fact]
    public async Task GetTagsAsync_NeverRecovers_CircuitOpensAfterThreeFailures()
    {
        // A stub that always returns 503 forces every call to fail.
        //
        // Pipeline sequence:
        //   Retry wraps → CircuitBreaker wraps → Timeout wraps → stub
        //
        // With MinimumThroughput=3 and FailureRatio=0.5:
        //   calls 1-3 reach the stub (each returns 503)
        //   after call 3 the circuit opens (100 % failure rate > 50 % threshold)
        //   call 4 (retry 3) is rejected by the circuit breaker before reaching the stub
        //   → BrokenCircuitException propagates; retry does NOT retry on it
        var fixture = new ResilienceFixture(alwaysFail: true);
        var service = fixture.Services.GetRequiredService<IExternalQuoteService>();

        var act = async () => await service.GetTagsAsync(quoteId: 99);

        // Either HttpRequestException (retries exhausted) or BrokenCircuitException
        // (circuit opened) signals the pipeline gave up — both are correct outcomes.
        await act.Should().ThrowAsync<Exception>(
            "Polly must surface a failure after all retries and circuit-breaker trips");

        // Only 3 calls reached the stub; the 4th was short-circuited by the CB
        fixture.Handler.CallCount.Should().Be(3,
            "circuit opened after 3 failures; the 4th attempt never reached the handler");

        await fixture.DisposeAsync();
    }
}

// ── Test doubles ─────────────────────────────────────────────────────────────

/// <summary>
/// Replaces the real HttpClientHandler so no network calls are made.
/// First <c>failCount</c> requests → 503 ServiceUnavailable.
/// Subsequent requests → 200 OK with <c>[]</c>.
/// When <c>alwaysFail</c> is true every request returns 503.
/// </summary>
internal sealed class TransientFailureHandler : HttpMessageHandler
{
    private int _callCount;
    private readonly int _failCount;
    private readonly bool _alwaysFail;

    public TransientFailureHandler(int failCount = 2, bool alwaysFail = false)
    {
        _failCount = failCount;
        _alwaysFail = alwaysFail;
    }

    public int CallCount => _callCount;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        var n = Interlocked.Increment(ref _callCount);
        var response = (_alwaysFail || n <= _failCount)
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : new HttpResponseMessage(HttpStatusCode.OK)
              {
                  Content = new StringContent("[]", Encoding.UTF8, "application/json")
              };
        return Task.FromResult(response);
    }
}

/// <summary>
/// WebApplicationFactory with:
///   • Isolated SQLite database per fixture instance
///   • TransientFailureHandler injected as the primary handler for "external-quotes"
/// </summary>
internal sealed class ResilienceFixture : WebApplicationFactory<Program>
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"resilience-{Guid.NewGuid():N}.db");

    public TransientFailureHandler Handler { get; }

    public ResilienceFixture(bool alwaysFail = false)
    {
        Handler = new TransientFailureHandler(failCount: 2, alwaysFail: alwaysFail);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Swap the real SQLite file for a temp one so tests are isolated.
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor is not null)
                services.Remove(descriptor);

            services.AddDbContext<AppDbContext>(opts =>
                opts.UseSqlite($"Data Source={_dbPath}"));

            // Replace the primary handler for the named "external-quotes" client.
            // The Polly resilience pipeline (retry/CB/timeout) is still wired up
            // above this stub — retries will cause the stub to be called multiple times.
            services.AddHttpClient(ExternalQuoteService.ClientName)
                .ConfigurePrimaryHttpMessageHandler(() => Handler);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Handler.Dispose();
        try { File.Delete(_dbPath); } catch { /* best-effort cleanup */ }
    }
}
