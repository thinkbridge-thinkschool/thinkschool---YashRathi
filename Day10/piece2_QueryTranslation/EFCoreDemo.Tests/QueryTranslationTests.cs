using EFCoreDemo.Data;
using EFCoreDemo.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EFCoreDemo.Tests;

/// <summary>
/// Verifies that .Select(dto) projections produce leaner SQL than whole-entity queries
/// and that accidental client-side evaluation is caught and fixed.
///
/// SQL shape is asserted via IQueryable.ToQueryString() — returns the SQL EF would
/// send without executing the query. Functional tests execute against an in-memory
/// SQLite fixture to confirm correct DTO values are returned.
/// </summary>
public sealed class QueryTranslationTests : IDisposable
{
    private readonly SqliteConnection              _conn;
    private readonly DbContextOptions<AppDbContext> _opts;

    public QueryTranslationTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();

        _opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_conn)
            .Options;

        using var ctx = NewContext();
        ctx.Database.EnsureCreated();
        SeedFixture(ctx);
    }

    public void Dispose() => _conn.Dispose();

    private AppDbContext NewContext() => new(_opts);

    private static void SeedFixture(AppDbContext ctx)
    {
        ctx.Products.AddRange(
            new Product { Name = "Laptop Pro",   Price = 1299.99m, Category = "Electronics", CreatedAt = new DateTime(2024, 1, 10, 0, 0, 0, DateTimeKind.Utc) },
            new Product { Name = "USB Hub",      Price =   29.99m, Category = "Electronics", CreatedAt = new DateTime(2024, 2, 5,  0, 0, 0, DateTimeKind.Utc) },
            new Product { Name = "Running Shoes",Price =   89.99m, Category = "Sports",      CreatedAt = new DateTime(2024, 3, 1,  0, 0, 0, DateTimeKind.Utc) }
        );
        ctx.SaveChanges();
    }

    // ── GROUP 1 — SQL shape: whole-entity query fetches ALL columns ──────────────

    [Fact]
    public void WholeEntity_ToQueryString_ContainsPriceColumn()
    {
        using var ctx = NewContext();

        var sql = ctx.Products
            .Where(p => p.Category == "Electronics")
            .AsNoTracking()
            .ToQueryString();

        Assert.Contains("Price", sql);
    }

    [Fact]
    public void WholeEntity_ToQueryString_ContainsCreatedAtColumn()
    {
        using var ctx = NewContext();

        var sql = ctx.Products
            .Where(p => p.Category == "Electronics")
            .AsNoTracking()
            .ToQueryString();

        Assert.Contains("CreatedAt", sql);
    }

    // ── GROUP 2 — SQL shape: DTO projection emits ONLY needed columns ─────────

    [Fact]
    public void Projected_ToQueryString_DoesNotContainPriceColumn()
    {
        using var ctx = NewContext();

        var sql = ctx.Products
            .Where(p => p.Category == "Electronics")
            .AsNoTracking()
            .Select(p => new ProductSummaryDto { Id = p.Id, Name = p.Name, Category = p.Category })
            .ToQueryString();

        Assert.DoesNotContain("Price", sql);
    }

    [Fact]
    public void Projected_ToQueryString_DoesNotContainCreatedAtColumn()
    {
        using var ctx = NewContext();

        var sql = ctx.Products
            .Where(p => p.Category == "Electronics")
            .AsNoTracking()
            .Select(p => new ProductSummaryDto { Id = p.Id, Name = p.Name, Category = p.Category })
            .ToQueryString();

        Assert.DoesNotContain("CreatedAt", sql);
    }

    [Fact]
    public void Projected_ToQueryString_ContainsIdNameCategoryColumns()
    {
        using var ctx = NewContext();

        var sql = ctx.Products
            .Where(p => p.Category == "Electronics")
            .AsNoTracking()
            .Select(p => new ProductSummaryDto { Id = p.Id, Name = p.Name, Category = p.Category })
            .ToQueryString();

        Assert.Contains("Id",       sql);
        Assert.Contains("Name",     sql);
        Assert.Contains("Category", sql);
    }

    // ── GROUP 3 — Client-eval bug: whole-entity SQL when .ToList() precedes .Select() ──

    [Fact]
    public void ClientEval_BugQuery_SqlFetchesAllColumns_BeforeToListBreaksTheChain()
    {
        // The bug: developer writes .ToList() before .Select(dto).
        // ToQueryString() on the IQueryable *before* .ToList() proves what SQL EF sends.
        // All five columns travel over the wire even though only three are used.
        using var ctx = NewContext();

        var queryBeforeClientSideBreak = ctx.Products
            .Where(p => p.Category == "Electronics")
            .AsNoTracking();

        var sql = queryBeforeClientSideBreak.ToQueryString();

        Assert.Contains("Price",     sql);
        Assert.Contains("CreatedAt", sql);
    }

    [Fact]
    public void ClientEval_FixedQuery_SqlDoesNotFetchPriceOrCreatedAt()
    {
        // The fix: .Select(dto) is placed BEFORE materialisation so EF translates
        // it to SQL, and Price/CreatedAt never leave the database engine.
        using var ctx = NewContext();

        var sql = ctx.Products
            .Where(p => p.Category == "Electronics")
            .AsNoTracking()
            .Select(p => new ProductSummaryDto { Id = p.Id, Name = p.Name, Category = p.Category })
            .ToQueryString();

        Assert.DoesNotContain("Price",     sql);
        Assert.DoesNotContain("CreatedAt", sql);
    }

    // ── GROUP 4 — Functional: projected query returns correct DTO values ────────

    [Fact]
    public async Task Projected_ReturnsCorrectDtoValues()
    {
        using var ctx = NewContext();

        var dtos = await ctx.Products
            .Where(p => p.Category == "Electronics")
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new ProductSummaryDto { Id = p.Id, Name = p.Name, Category = p.Category })
            .ToListAsync();

        Assert.Equal(2, dtos.Count);
        Assert.All(dtos, d => Assert.Equal("Electronics", d.Category));
        Assert.All(dtos, d => Assert.True(d.Id > 0));
        Assert.All(dtos, d => Assert.False(string.IsNullOrEmpty(d.Name)));
    }

    [Fact]
    public async Task Projected_DtoHasNoPrice_NorCreatedAt_Properties()
    {
        // Compile-time proof: ProductSummaryDto only has Id, Name, Category.
        // This test documents the intentional omission.
        using var ctx = NewContext();

        var dto = await ctx.Products
            .AsNoTracking()
            .Select(p => new ProductSummaryDto { Id = p.Id, Name = p.Name, Category = p.Category })
            .FirstAsync();

        var props = typeof(ProductSummaryDto).GetProperties().Select(p => p.Name).ToHashSet();

        Assert.DoesNotContain("Price",     props);
        Assert.DoesNotContain("CreatedAt", props);
    }

    [Fact]
    public async Task ClientEval_Fixed_ReturnsCorrectResults()
    {
        // End-to-end: fixed query (Select before ToList) returns the same data as
        // the buggy query, but via server-side projection.
        using var ctx = NewContext();

        var dtos = await ctx.Products
            .Where(p => p.Category == "Sports")
            .AsNoTracking()
            .Select(p => new ProductSummaryDto { Id = p.Id, Name = p.Name, Category = p.Category })
            .ToListAsync();

        Assert.Single(dtos);
        Assert.Equal("Running Shoes", dtos[0].Name);
        Assert.Equal("Sports",        dtos[0].Category);
    }

    // ── GROUP 5 — Edge case: empty result set returns empty list, not exception ─

    [Fact]
    public async Task Projected_NoMatchingRows_ReturnsEmptyList()
    {
        using var ctx = NewContext();

        var dtos = await ctx.Products
            .Where(p => p.Category == "NonExistent")
            .AsNoTracking()
            .Select(p => new ProductSummaryDto { Id = p.Id, Name = p.Name, Category = p.Category })
            .ToListAsync();

        Assert.Empty(dtos);
    }
}
