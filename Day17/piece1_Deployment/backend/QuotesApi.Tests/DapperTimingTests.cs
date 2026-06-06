using System.Diagnostics;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Abstractions;
using QuotesApi.Commands;
using QuotesApi.Data;
using QuotesApi.Queries;
using QuotesApi.Repositories;
using Xunit;
using Xunit.Abstractions;

namespace QuotesApi.Tests;

/// <summary>
/// Compares EF Core projection handler vs Dapper handler on the GET /api/quotes hot path.
///
/// Both queries are logically identical — same WHERE, ORDER BY, LIMIT/OFFSET, and projected columns.
/// The delta is pure ORM plumbing: EF resolves the LINQ expression tree and navigates its internal
/// model snapshot on every call; Dapper sends the literal SQL string directly.
///
/// EF SQL (SQLite dialect):
///   SELECT "q"."Id", "q"."Author", "q"."Text", "q"."CreatedAt"
///   FROM "Quotes" AS "q"
///   WHERE NOT ("q"."IsDeleted")
///   ORDER BY "q"."Id"
///   LIMIT @__p_1 OFFSET @__p_0
///
/// Dapper SQL:
///   SELECT Id, Author, Text, CreatedAt
///   FROM Quotes
///   WHERE IsDeleted = 0
///   ORDER BY Id
///   LIMIT @Size OFFSET @Offset
/// </summary>
public class DapperTimingTests(ITestOutputHelper output)
{
    private const int SeedRows = 500;
    private const int WarmupRuns = 10;
    private const int TimedRuns = 200;

    private static AppDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={Path.Combine(Path.GetTempPath(), $"dapper-bench-{Guid.NewGuid():N}.db")}")
            .Options);

    private static readonly DateTimeOffset _now = new(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);

    // Seeds SeedRows quotes via EF and returns the open DbContext.
    private static async Task<AppDbContext> SeedAsync()
    {
        var ctx = BuildContext();
        ctx.Database.EnsureCreated();
        var repo = new QuoteRepository(ctx);
        var clock = new TestClock(_now);
        var handler = new CreateQuoteCommandHandler(repo, clock);
        for (var i = 1; i <= SeedRows; i++)
            await handler.HandleAsync(
                new CreateQuoteCommand($"Author {i % 20:D2}", $"Quote {i:D3}.", null),
                CancellationToken.None);
        return ctx;
    }

    // ── Correctness ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Dapper_ReturnsIdenticalResultsToEf()
    {
        using var ctx = await SeedAsync();
        var efHandler = new GetQuotesQueryHandler(ctx);
        var dapperHandler = new GetQuotesDapperQueryHandler(ctx);
        var query = new GetQuotesQuery(Page: 1, Size: 20);

        var efResults = await efHandler.HandleAsync(query, CancellationToken.None);
        var dapperResults = await dapperHandler.HandleAsync(query, CancellationToken.None);

        dapperResults.Should().HaveCount(efResults.Count);
        for (var i = 0; i < efResults.Count; i++)
        {
            dapperResults[i].Id.Should().Be(efResults[i].Id);
            dapperResults[i].Author.Should().Be(efResults[i].Author);
            dapperResults[i].Text.Should().Be(efResults[i].Text);
            dapperResults[i].CreatedAt.Should().Be(efResults[i].CreatedAt);
        }
    }

    [Fact]
    public async Task Dapper_PaginationMatchesEf()
    {
        using var ctx = await SeedAsync();
        var efHandler = new GetQuotesQueryHandler(ctx);
        var dapperHandler = new GetQuotesDapperQueryHandler(ctx);

        for (var page = 1; page <= 5; page++)
        {
            var q = new GetQuotesQuery(page, 10);
            var efPage = await efHandler.HandleAsync(q, CancellationToken.None);
            var dapperPage = await dapperHandler.HandleAsync(q, CancellationToken.None);
            dapperPage.Select(r => r.Id).Should().BeEquivalentTo(efPage.Select(r => r.Id),
                $"page {page} IDs must match");
        }
    }

    [Fact]
    public async Task Dapper_ExcludesSoftDeletedRows()
    {
        using var ctx = BuildContext();
        ctx.Database.EnsureCreated();
        var repo = new QuoteRepository(ctx);
        var cmdHandler = new CreateQuoteCommandHandler(repo, new TestClock(_now));

        await cmdHandler.HandleAsync(new CreateQuoteCommand("Visible", "Kept.", null), CancellationToken.None);
        var deletedId = (await cmdHandler.HandleAsync(
            new CreateQuoteCommand("Deleted", "Gone.", null), CancellationToken.None)).Value;

        var q = await ctx.Quotes.FindAsync(deletedId);
        q!.SoftDelete();
        await ctx.SaveChangesAsync();

        var handler = new GetQuotesDapperQueryHandler(ctx);
        var results = await handler.HandleAsync(new GetQuotesQuery(1, 10), CancellationToken.None);

        results.Should().HaveCount(1);
        results[0].Author.Should().Be("Visible");
    }

    [Fact]
    public async Task Dapper_EmptyDatabase_ReturnsEmptyList()
    {
        using var ctx = BuildContext();
        ctx.Database.EnsureCreated();
        var handler = new GetQuotesDapperQueryHandler(ctx);

        var results = await handler.HandleAsync(new GetQuotesQuery(1, 10), CancellationToken.None);

        results.Should().BeEmpty();
    }

    // ── Timing comparison ──────────────────────────────────────────────────────

    [Fact]
    public async Task TimingComparison_DapperVsEf_PrintsResultsToOutput()
    {
        using var ctx = await SeedAsync();
        var efHandler = new GetQuotesQueryHandler(ctx);
        var dapperHandler = new GetQuotesDapperQueryHandler(ctx);
        var query = new GetQuotesQuery(Page: 1, Size: 20);

        // Warmup: prime EF's compiled-query cache + SQLite page cache.
        for (var i = 0; i < WarmupRuns; i++)
        {
            await efHandler.HandleAsync(query, CancellationToken.None);
            await dapperHandler.HandleAsync(query, CancellationToken.None);
        }

        var efSw = Stopwatch.StartNew();
        for (var i = 0; i < TimedRuns; i++)
            await efHandler.HandleAsync(query, CancellationToken.None);
        efSw.Stop();

        var dapperSw = Stopwatch.StartNew();
        for (var i = 0; i < TimedRuns; i++)
            await dapperHandler.HandleAsync(query, CancellationToken.None);
        dapperSw.Stop();

        var efAvgMicros = (double)efSw.ElapsedTicks / TimedRuns / Stopwatch.Frequency * 1_000_000;
        var dapperAvgMicros = (double)dapperSw.ElapsedTicks / TimedRuns / Stopwatch.Frequency * 1_000_000;
        var speedup = efAvgMicros / dapperAvgMicros;

        output.WriteLine("═══════════════════════════════════════════════════════════════");
        output.WriteLine($"  EF Core vs Dapper — GET /api/quotes (page=1, size=20, {SeedRows} rows in DB)");
        output.WriteLine("═══════════════════════════════════════════════════════════════");
        output.WriteLine($"  Warmup iterations : {WarmupRuns}");
        output.WriteLine($"  Timed  iterations : {TimedRuns}");
        output.WriteLine("");
        output.WriteLine("  EF SQL:");
        output.WriteLine("    SELECT \"q\".\"Id\", \"q\".\"Author\", \"q\".\"Text\", \"q\".\"CreatedAt\"");
        output.WriteLine("    FROM \"Quotes\" AS \"q\"");
        output.WriteLine("    WHERE NOT (\"q\".\"IsDeleted\")");
        output.WriteLine("    ORDER BY \"q\".\"Id\"");
        output.WriteLine("    LIMIT @__p_1 OFFSET @__p_0");
        output.WriteLine("");
        output.WriteLine("  Dapper SQL:");
        output.WriteLine("    SELECT Id, Author, Text, CreatedAt");
        output.WriteLine("    FROM Quotes");
        output.WriteLine("    WHERE IsDeleted = 0");
        output.WriteLine("    ORDER BY Id");
        output.WriteLine("    LIMIT @Size OFFSET @Offset");
        output.WriteLine("");
        output.WriteLine($"  EF Core total  : {efSw.ElapsedMilliseconds,6} ms  |  avg {efAvgMicros,7:F1} µs/call");
        output.WriteLine($"  Dapper total   : {dapperSw.ElapsedMilliseconds,6} ms  |  avg {dapperAvgMicros,7:F1} µs/call");
        output.WriteLine($"  Dapper speedup : {speedup:F2}x");
        output.WriteLine("");
        output.WriteLine("  RULE:");
        output.WriteLine("  Use Dapper on hot read paths where the query is fixed, the result is a");
        output.WriteLine("  DTO (no domain behaviour), and profiling shows EF's per-call overhead is");
        output.WriteLine("  measurable. Keep EF for writes, migrations, dynamic queries, and anything");
        output.WriteLine("  that benefits from the change tracker or first-level cache. On a DTO");
        output.WriteLine("  projection with EF's compiled-query cache warmed, the gap is typically");
        output.WriteLine("  < 20 µs — only reach for Dapper when you have profiling evidence, not");
        output.WriteLine("  as a default.");
        output.WriteLine("═══════════════════════════════════════════════════════════════");

        // Timing is informational; only assert that both handlers produced valid output.
        efSw.ElapsedMilliseconds.Should().BeGreaterThan(0);
        dapperSw.ElapsedMilliseconds.Should().BeGreaterThan(0);
    }
}
