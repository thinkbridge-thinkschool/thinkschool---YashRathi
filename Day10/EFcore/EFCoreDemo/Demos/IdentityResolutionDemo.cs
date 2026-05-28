using System.Runtime.CompilerServices;
using EFCoreDemo.Data;
using EFCoreDemo.Models;
using Microsoft.EntityFrameworkCore;

namespace EFCoreDemo.Demos;

public static class IdentityResolutionDemo
{
    public static async Task RunAsync(Func<AppDbContext> contextFactory)
    {
        PrintHeader("PART 3 — Identity Resolution Demo");

        await DemoTracked(contextFactory);
        Console.WriteLine();
        await DemoAsNoTracking(contextFactory);
        Console.WriteLine();
        await DemoAsNoTrackingWithIdentityResolution(contextFactory);
        Console.WriteLine();
        ExplainWhyItExists();
    }

    // ── A: Tracked — same key returns same C# object reference ─────────────

    private static async Task DemoTracked(Func<AppDbContext> contextFactory)
    {
        Console.WriteLine("┌────────────────────────────────────────────────────────────────┐");
        Console.WriteLine("│  A: Tracked — same key queried twice                           │");
        Console.WriteLine("└────────────────────────────────────────────────────────────────┘");

        using var ctx = contextFactory();

        int id = await ctx.Products.Select(p => p.Id).FirstAsync();

        var first  = await ctx.Products.FirstAsync(p => p.Id == id);
        var second = await ctx.Products.FirstAsync(p => p.Id == id);

        bool same = ReferenceEquals(first, second);

        Console.WriteLine($"  Id targeted             : {id}");
        Console.WriteLine($"  first  RuntimeHash      : {RuntimeHelpers.GetHashCode(first),10}");
        Console.WriteLine($"  second RuntimeHash      : {RuntimeHelpers.GetHashCode(second),10}");
        Console.WriteLine($"  ReferenceEquals(a, b)   : {same}  (expected: True)");
        Console.WriteLine($"  ChangeTracker.Entries   : {ctx.ChangeTracker.Entries<Product>().Count()}  (only 1 — second query was served from identity map, no DB round-trip)");
        Console.WriteLine();
        Console.WriteLine("  WHY: EF Core's identity map (a Dictionary<IKey,object> inside the");
        Console.WriteLine("       ChangeTracker) returns the cached instance when the same key");
        Console.WriteLine("       is seen again. The second SQL query IS still sent to the DB,");
        Console.WriteLine("       but the materializer looks up the key and returns the existing");
        Console.WriteLine("       C# object instead of allocating a new one.");
    }

    // ── B: AsNoTracking — new object per query ───────────────────────────────

    private static async Task DemoAsNoTracking(Func<AppDbContext> contextFactory)
    {
        Console.WriteLine("┌────────────────────────────────────────────────────────────────┐");
        Console.WriteLine("│  B: AsNoTracking — same key queried twice                      │");
        Console.WriteLine("└────────────────────────────────────────────────────────────────┘");

        using var ctx = contextFactory();

        int id = await ctx.Products.Select(p => p.Id).FirstAsync();

        var first  = await ctx.Products.AsNoTracking().FirstAsync(p => p.Id == id);
        var second = await ctx.Products.AsNoTracking().FirstAsync(p => p.Id == id);

        bool same = ReferenceEquals(first, second);

        Console.WriteLine($"  Id targeted             : {id}");
        Console.WriteLine($"  first  RuntimeHash      : {RuntimeHelpers.GetHashCode(first),10}");
        Console.WriteLine($"  second RuntimeHash      : {RuntimeHelpers.GetHashCode(second),10}");
        Console.WriteLine($"  ReferenceEquals(a, b)   : {same}  (expected: False)");
        Console.WriteLine($"  ChangeTracker.Entries   : {ctx.ChangeTracker.Entries<Product>().Count()}  (always 0 — nothing tracked)");
        Console.WriteLine();
        Console.WriteLine("  WHY: Without an identity map, EF materializes a fresh heap object");
        Console.WriteLine("       on every query. Two queries for the same row = two distinct");
        Console.WriteLine("       C# objects with identical data but different addresses.");
    }

    // ── C: AsNoTrackingWithIdentityResolution ────────────────────────────────

    private static async Task DemoAsNoTrackingWithIdentityResolution(Func<AppDbContext> contextFactory)
    {
        Console.WriteLine("┌────────────────────────────────────────────────────────────────┐");
        Console.WriteLine("│  C: AsNoTrackingWithIdentityResolution                         │");
        Console.WriteLine("└────────────────────────────────────────────────────────────────┘");

        using var ctx = contextFactory();

        int id = await ctx.Products.Select(p => p.Id).FirstAsync();

        // Separate query calls — the identity map is scoped per-query, NOT per-context
        var first  = await ctx.Products.AsNoTrackingWithIdentityResolution().FirstAsync(p => p.Id == id);
        var second = await ctx.Products.AsNoTrackingWithIdentityResolution().FirstAsync(p => p.Id == id);

        bool separateSame = ReferenceEquals(first, second);

        Console.WriteLine($"  Two SEPARATE queries for same Id:");
        Console.WriteLine($"  first  RuntimeHash      : {RuntimeHelpers.GetHashCode(first),10}");
        Console.WriteLine($"  second RuntimeHash      : {RuntimeHelpers.GetHashCode(second),10}");
        Console.WriteLine($"  ReferenceEquals         : {separateSame}  (expected: False — different query scopes)");
        Console.WriteLine();

        // Single bulk query — within ONE ToList() call, the identity map is active
        // and will de-duplicate parent references when the same entity key appears
        // multiple times in a JOIN result (e.g. via Include with 1:N navigation).
        //
        // Without a navigation property on Product we can demonstrate the memory
        // allocations differ, but reference equality within a flat ToList is
        // irrelevant since each row maps to a unique product anyway.
        //
        // The meaningful demo is the Include() scenario explained below.

        var batchAtn   = await ctx.Products.AsNoTracking()
            .OrderBy(p => p.Id).Take(10).ToListAsync();

        using var ctx2 = contextFactory();
        var batchAtnwir = await ctx2.Products.AsNoTrackingWithIdentityResolution()
            .OrderBy(p => p.Id).Take(10).ToListAsync();

        Console.WriteLine($"  Single flat batch (10 rows):");
        Console.WriteLine($"  AsNoTracking           loaded: {batchAtn.Count} distinct objects");
        Console.WriteLine($"  ATNWIR                 loaded: {batchAtnwir.Count} distinct objects");
        Console.WriteLine();
        Console.WriteLine("  For flat queries both look identical. The REAL advantage appears");
        Console.WriteLine("  in a JOIN with Include():  Orders.AsNoTrackingWithIdentityResolution()");
        Console.WriteLine("                               .Include(o => o.Product).ToList()");
        Console.WriteLine("  Without ATNWIR: 50 orders sharing 1 product → 50 Product objects.");
        Console.WriteLine("  With    ATNWIR: 50 orders sharing 1 product → 1 Product object.");
    }

    private static void ExplainWhyItExists()
    {
        Console.WriteLine("┌────────────────────────────────────────────────────────────────┐");
        Console.WriteLine("│  WHY AsNoTrackingWithIdentityResolution EXISTS                 │");
        Console.WriteLine("└────────────────────────────────────────────────────────────────┘");
        Console.WriteLine();
        Console.WriteLine("  Problem: A 1:N Include() query with AsNoTracking allocates a");
        Console.WriteLine("           fresh parent entity per child row. If Product 42 has 100");
        Console.WriteLine("           Reviews, you get 100 duplicate Product instances — all");
        Console.WriteLine("           identical, all wasting heap space.");
        Console.WriteLine();
        Console.WriteLine("  Solution: ATNWIR builds a temporary identity map that lives for");
        Console.WriteLine("            the duration of ONE query. It de-duplicates parent refs");
        Console.WriteLine("            within that result set WITHOUT adding them to the");
        Console.WriteLine("            ChangeTracker. Objects remain untracked — SaveChanges()");
        Console.WriteLine("            still ignores them.");
        Console.WriteLine();
        Console.WriteLine("  Memory tradeoff:");
        Console.WriteLine("    + Prevents N duplicate parent objects in a high fan-out JOIN.");
        Console.WriteLine("    - Allocates a Dictionary<IKey,object> for every query call.");
        Console.WriteLine("    → Use ATNWIR only with Include() / navigation-heavy queries.");
        Console.WriteLine("    → Use plain AsNoTracking() for flat single-table reads.");
        Console.WriteLine("    → Use default tracking whenever you plan to modify + save.");
    }

    private static void PrintHeader(string title)
    {
        Console.WriteLine();
        Console.WriteLine(new string('═', 65));
        Console.WriteLine($"  {title}");
        Console.WriteLine(new string('═', 65));
    }
}
