using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Data;
using QuotesApi.Models;
using QuotesApi.Repositories;
using Xunit;

namespace QuotesApi.Tests;

/// <summary>
/// Verifies the N+1 fix on /api/quotes/by-author-fast:
///   - Returns identical data to the slow endpoint
///   - Fires exactly 1 SQL SELECT (not N+1)
///   - The slow path demonstrably fires N+1 queries
/// </summary>
public class ByAuthorQueryTests : IAsyncLifetime
{
    private readonly ApiFixture _fixture = new();
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _client = _fixture.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _fixture.DisposeAsync();
    }

    // ── endpoint correctness ────────────────────────────────────────────────

    [Fact]
    public async Task FastEndpoint_Returns200WithAuthorDictionary()
    {
        var resp = await _client.GetAsync("/api/quotes/by-author-fast");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Object,
            "response must be a JSON object keyed by author name");
    }

    [Fact]
    public async Task FastEndpoint_ReturnsSameDataAsSlowEndpoint()
    {
        var slowResp = await _client.GetAsync("/api/quotes/by-author");
        var fastResp = await _client.GetAsync("/api/quotes/by-author-fast");

        slowResp.StatusCode.Should().Be(HttpStatusCode.OK);
        fastResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var slow = await slowResp.Content.ReadFromJsonAsync<Dictionary<string, List<string>>>();
        var fast = await fastResp.Content.ReadFromJsonAsync<Dictionary<string, List<string>>>();

        slow.Should().NotBeNull();
        fast.Should().NotBeNull();
        fast!.Keys.Should().BeEquivalentTo(slow!.Keys,
            "both paths must return the same set of authors");

        foreach (var author in slow.Keys)
        {
            fast[author].Should().BeEquivalentTo(slow[author],
                $"quotes for author '{author}' must be identical");
        }
    }

    [Fact]
    public async Task FastEndpoint_ContainsSeededAuthors()
    {
        var resp = await _client.GetAsync("/api/quotes/by-author-fast");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var data = await resp.Content.ReadFromJsonAsync<Dictionary<string, List<string>>>();

        data.Should().NotBeNull();
        data!.Count.Should().Be(20, "seed creates 20 distinct authors");
        data.Values.Should().AllSatisfy(quotes =>
            quotes.Count.Should().Be(10, "each author has 10 seeded quotes"));
    }

    // ── cache correctness ───────────────────────────────────────────────────

    [Fact]
    public async Task FastEndpoint_SecondCall_ReturnsSameDataAsCachedFirst()
    {
        // First call populates the cache.
        var first = await _client.GetAsync("/api/quotes/by-author-fast");
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstBody = await first.Content.ReadAsStringAsync();

        // Second call is served from IMemoryCache — must be identical.
        var second = await _client.GetAsync("/api/quotes/by-author-fast");
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondBody = await second.Content.ReadAsStringAsync();

        secondBody.Should().Be(firstBody,
            "cached response must be byte-for-byte identical to the DB response");
    }

    // ── query count verification ────────────────────────────────────────────

    [Fact]
    public async Task FastRepository_FiresExactlyOneQuery()
    {
        var interceptor = new SelectCountInterceptor();
        using var context = BuildContext(interceptor);
        await SeedContextAsync(context);
        interceptor.Reset(); // clear AnyAsync() from seed

        var repo = new QuoteRepository(context);
        await repo.GetByAuthorFastAsync(CancellationToken.None);

        interceptor.SelectCount.Should().Be(1,
            "GetByAuthorFastAsync must issue a single SELECT");
    }

    [Fact]
    public async Task SlowRepository_FiresNPlusOneQueries()
    {
        const int expectedAuthors = 3;
        var interceptor = new SelectCountInterceptor();
        using var context = BuildContext(interceptor);
        await SeedContextAsync(context, authorCount: expectedAuthors, quotesPerAuthor: 2);
        interceptor.Reset(); // clear AnyAsync() from seed

        var repo = new QuoteRepository(context);
        await repo.GetByAuthorSlowAsync(CancellationToken.None);

        // 1 DISTINCT query + 1 per author
        interceptor.SelectCount.Should().Be(expectedAuthors + 1,
            $"GetByAuthorSlowAsync fires 1 DISTINCT + {expectedAuthors} per-author SELECTs");
    }

    [Fact]
    public async Task FastRepository_QueryCountDoesNotGrowWithAuthors()
    {
        // With 10 authors the slow path fires 11 queries, the fast path still fires 1.
        var interceptor = new SelectCountInterceptor();
        using var context = BuildContext(interceptor);
        await SeedContextAsync(context, authorCount: 10, quotesPerAuthor: 5);
        interceptor.Reset(); // clear AnyAsync() from seed

        var repo = new QuoteRepository(context);
        await repo.GetByAuthorFastAsync(CancellationToken.None);

        interceptor.SelectCount.Should().Be(1,
            "query count must remain constant regardless of author count");
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private static AppDbContext BuildContext(SelectCountInterceptor interceptor)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={Path.Combine(Path.GetTempPath(), $"query-test-{Guid.NewGuid():N}.db")}")
            .AddInterceptors(interceptor)
            .Options;

        var ctx = new AppDbContext(options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static async Task SeedContextAsync(
        AppDbContext context,
        int authorCount = 20,
        int quotesPerAuthor = 10)
    {
        if (await context.Quotes.AnyAsync()) return;

        var now = DateTimeOffset.UtcNow;
        var quotes = new List<Quote>();
        for (var a = 1; a <= authorCount; a++)
        {
            var name = $"Author {a:D2}";
            for (var q = 1; q <= quotesPerAuthor; q++)
            {
                var result = Quote.Create(name, $"Quote {q} by {name}.", now);
                if (result.IsSuccess) quotes.Add(result.Value!);
            }
        }
        context.Quotes.AddRange(quotes);
        await context.SaveChangesAsync();
    }
}

/// <summary>
/// Counts executed SELECT commands via EF Core's interceptor API.
/// Increments on ReaderExecuted* (async path) only — ignores SaveChanges INSERTs.
/// </summary>
internal sealed class SelectCountInterceptor : DbCommandInterceptor
{
    private int _count;
    public int SelectCount => _count;
    public void Reset() => Interlocked.Exchange(ref _count, 0);

    public override DbDataReader ReaderExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result)
    {
        if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            Interlocked.Increment(ref _count);
        return result;
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            Interlocked.Increment(ref _count);
        return new ValueTask<DbDataReader>(result);
    }
}
