using EFCoreDemo.Data;
using EFCoreDemo.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EFCoreDemo.Demos;

public static class QueryTranslationDemo
{
    // contextFactory receives a collector list — caller wires up LogTo to append SQL lines.
    public static async Task RunAsync(Func<List<string>, AppDbContext> loggingContextFactory)
    {
        PrintHeader("PART 5 — Query Translation + Projections");

        await Part1_WholeEntityQuery(loggingContextFactory);
        Console.WriteLine();
        await Part2_ProjectedDtoQuery(loggingContextFactory);
        Console.WriteLine();
        await Part3_ClientEvalCaughtAndFixed(loggingContextFactory);
    }

    // ── PART 1 ─────────────────────────────────────────────────────────────────
    // Whole-entity query: EF selects every column even if the caller uses two.

    private static async Task Part1_WholeEntityQuery(Func<List<string>, AppDbContext> factory)
    {
        Console.WriteLine("PART 1 — Whole-entity query (SELECT *)");

        var sqlLines = new List<string>();
        using var ctx = factory(sqlLines);

        var products = await ctx.Products
            .Where(p => p.Category == "Electronics")
            .AsNoTracking()
            .Take(3)
            .ToListAsync();

        var generatedSql = ExtractSelectStatement(sqlLines);

        Console.WriteLine();
        Console.WriteLine("  C# query:");
        Console.WriteLine("    ctx.Products");
        Console.WriteLine("        .Where(p => p.Category == \"Electronics\")");
        Console.WriteLine("        .AsNoTracking()");
        Console.WriteLine("        .Take(3)");
        Console.WriteLine("        .ToListAsync()");
        Console.WriteLine();
        Console.WriteLine("  SQL sent to DB:");
        Console.WriteLine($"    {generatedSql}");
        Console.WriteLine();
        Console.WriteLine("  Columns fetched : Id, Name, Price, Category, CreatedAt  (ALL 5)");
        Console.WriteLine($"  Rows returned   : {products.Count}");
        Console.WriteLine();
        Console.WriteLine("  PROBLEM: Price and CreatedAt are never used by the caller, yet");
        Console.WriteLine("  every row transfers those bytes over the network / from disk.");
        Console.WriteLine("  On a 10k-row table, that is wasted I/O on every request.");
    }

    // ── PART 2 ─────────────────────────────────────────────────────────────────
    // Projected query: .Select(dto) is translated to SQL — only needed columns fetched.

    private static async Task Part2_ProjectedDtoQuery(Func<List<string>, AppDbContext> factory)
    {
        Console.WriteLine("PART 2 — Projected DTO query (.Select before materialisation)");

        var sqlLines = new List<string>();
        using var ctx = factory(sqlLines);

        var dtos = await ctx.Products
            .Where(p => p.Category == "Electronics")
            .AsNoTracking()
            .Select(p => new ProductSummaryDto
            {
                Id       = p.Id,
                Name     = p.Name,
                Category = p.Category
            })
            .Take(3)
            .ToListAsync();

        var generatedSql = ExtractSelectStatement(sqlLines);

        Console.WriteLine();
        Console.WriteLine("  C# query:");
        Console.WriteLine("    ctx.Products");
        Console.WriteLine("        .Where(p => p.Category == \"Electronics\")");
        Console.WriteLine("        .AsNoTracking()");
        Console.WriteLine("        .Select(p => new ProductSummaryDto { Id, Name, Category })");
        Console.WriteLine("        .Take(3)");
        Console.WriteLine("        .ToListAsync()");
        Console.WriteLine();
        Console.WriteLine("  SQL sent to DB:");
        Console.WriteLine($"    {generatedSql}");
        Console.WriteLine();
        Console.WriteLine("  Columns fetched : Id, Name, Category  (3 of 5 — Price + CreatedAt GONE)");
        Console.WriteLine($"  Rows returned   : {dtos.Count}");
        Console.WriteLine();
        Console.WriteLine("  GAIN: EF translated the .Select() to a SQL projection.");
        Console.WriteLine("  Price and CreatedAt are not referenced — they never leave the DB engine.");
        Console.WriteLine("  Fewer bytes per row = less I/O, less memory allocation, faster response.");
    }

    // ── PART 3 ─────────────────────────────────────────────────────────────────
    // Client-side evaluation caught: .ToList() before .Select(dto) breaks the LINQ
    // chain. EF materialises the full entity first; the projection then runs in C#.

    private static async Task Part3_ClientEvalCaughtAndFixed(Func<List<string>, AppDbContext> factory)
    {
        Console.WriteLine("PART 3 — Client-side evaluation: bug caught and fixed");
        Console.WriteLine();

        // ── The Bug ──────────────────────────────────────────────────────────────
        Console.WriteLine("  [BUG] .ToList() is called before .Select(dto):");
        Console.WriteLine();
        Console.WriteLine("    var result = ctx.Products");
        Console.WriteLine("                     .Where(p => p.Category == \"Electronics\")");
        Console.WriteLine("                     .AsNoTracking()");
        Console.WriteLine("                     .ToList()           // ← DB call here (SELECT *)");
        Console.WriteLine("                     .Select(p => new ProductSummaryDto { ... })");
        Console.WriteLine("                     .ToList();          // ← C# LINQ, no SQL");

        var bugSqlLines = new List<string>();
        using (var ctx = factory(bugSqlLines))
        {
            // Materialise first — this is the bug pattern
            _ = ctx.Products
                    .Where(p => p.Category == "Electronics")
                    .AsNoTracking()
                    .ToList()
                    .Select(p => new ProductSummaryDto
                    {
                        Id       = p.Id,
                        Name     = p.Name,
                        Category = p.Category
                    })
                    .ToList();
        }

        var bugSql = ExtractSelectStatement(bugSqlLines);
        Console.WriteLine();
        Console.WriteLine("  SQL actually sent (from the early .ToList()):");
        Console.WriteLine($"    {bugSql}");
        Console.WriteLine();
        Console.WriteLine("  ALL columns fetched. Price and CreatedAt travel from DB to app");
        Console.WriteLine("  even though .Select(dto) discards them immediately after.");
        Console.WriteLine("  No exception is raised — this silently wastes I/O on every call.");

        Console.WriteLine();
        Console.WriteLine("  ──────────────────────────────────────────────────────────────");
        Console.WriteLine();

        // ── The Fix ──────────────────────────────────────────────────────────────
        Console.WriteLine("  [FIX] Move .Select(dto) BEFORE .ToListAsync():");
        Console.WriteLine();
        Console.WriteLine("    var result = await ctx.Products");
        Console.WriteLine("                              .Where(p => p.Category == \"Electronics\")");
        Console.WriteLine("                              .AsNoTracking()");
        Console.WriteLine("                              .Select(p => new ProductSummaryDto { ... })");
        Console.WriteLine("                              .ToListAsync();  // ← SQL projection here");

        var fixSqlLines = new List<string>();
        using (var ctx = factory(fixSqlLines))
        {
            _ = await ctx.Products
                    .Where(p => p.Category == "Electronics")
                    .AsNoTracking()
                    .Select(p => new ProductSummaryDto
                    {
                        Id       = p.Id,
                        Name     = p.Name,
                        Category = p.Category
                    })
                    .ToListAsync();
        }

        var fixSql = ExtractSelectStatement(fixSqlLines);
        Console.WriteLine();
        Console.WriteLine("  SQL actually sent (after the fix):");
        Console.WriteLine($"    {fixSql}");
        Console.WriteLine();
        Console.WriteLine("  Price and CreatedAt are GONE from the SQL.");
        Console.WriteLine("  The DB engine does the projection — only three columns travel.");
        Console.WriteLine();
        Console.WriteLine("  ROOT CAUSE: LINQ's .Select() is lazy on IEnumerable (C#) but");
        Console.WriteLine("  translatable on IQueryable (EF). Calling .ToList() downgrades");
        Console.WriteLine("  IQueryable → IEnumerable, so every operator after it runs in C#.");
        Console.WriteLine("  Rule: keep the LINQ chain on IQueryable until you call .ToListAsync().");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static string ExtractSelectStatement(List<string> logLines)
    {
        // LogTo emits multi-line entries; the SELECT keyword starts the SQL block.
        var selectLine = logLines
            .SelectMany(l => l.Split('\n'))
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase));

        return selectLine ?? "(SQL not captured — check LogTo configuration)";
    }

    private static void PrintHeader(string title)
    {
        Console.WriteLine();
        Console.WriteLine(new string('═', 65));
        Console.WriteLine($"  {title}");
        Console.WriteLine(new string('═', 65));
    }
}
