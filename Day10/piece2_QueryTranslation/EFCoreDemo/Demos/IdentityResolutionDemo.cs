using System.Runtime.CompilerServices;
using EFCoreDemo.Data;
using EFCoreDemo.Models;
using Microsoft.EntityFrameworkCore;

namespace EFCoreDemo.Demos;

public static class IdentityResolutionDemo
{
    public static async Task RunAsync(Func<AppDbContext> contextFactory)
    {
        PrintHeader("PART 2 — Identity Resolution Demo");

        await DemoTracked(contextFactory);
        Console.WriteLine();
        await DemoAsNoTracking(contextFactory);
        Console.WriteLine();
        await DemoAsNoTrackingWithIdentityResolution(contextFactory);
        Console.WriteLine();
        ExplainWhyItExists();
    }

    //  A: Tracked — same key returns same C# object reference 

    private static async Task DemoTracked(Func<AppDbContext> contextFactory)
    {
        Console.WriteLine("  A: Tracked — same key queried twice");

        using var ctx = contextFactory();

        int id = await ctx.Products.Select(p => p.Id).FirstAsync();

        var first  = await ctx.Products.FirstAsync(p => p.Id == id);
        var second = await ctx.Products.FirstAsync(p => p.Id == id);

        bool same = ReferenceEquals(first, second);

        Console.WriteLine($"  Id targeted             : {id}");
        Console.WriteLine($"  first  RuntimeHash      : {RuntimeHelpers.GetHashCode(first),10}");
        Console.WriteLine($"  second RuntimeHash      : {RuntimeHelpers.GetHashCode(second),10}");
        Console.WriteLine($"  ReferenceEquals(a, b)   : {same}  (expected: True)");
        Console.WriteLine($"  ChangeTracker.Entries   : {ctx.ChangeTracker.Entries<Product>().Count()}  (only 1 — both queries hit DB; identity map de-duplicates the C# object, not the SQL)");
        Console.WriteLine();
        Console.WriteLine("  WHY: The identity map returns the same instance for the same key.");
        Console.WriteLine("       SQL is still sent, but EF hands back the cached object — no new allocation.");
    }

    // B: AsNoTracking — new object per query 

    private static async Task DemoAsNoTracking(Func<AppDbContext> contextFactory)
    {
        Console.WriteLine("B: AsNoTracking — same key queried twice ");

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
        Console.WriteLine("  WHY: No identity map = new object every query.");
        Console.WriteLine("       Same row, same data, but two different C# instances.");
    }

    //  C: AsNoTrackingWithIdentityResolution

    private static async Task DemoAsNoTrackingWithIdentityResolution(Func<AppDbContext> contextFactory)
    {
        Console.WriteLine(" C: AsNoTrackingWithIdentityResolution");

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

        // flat query: ATNWIR makes no visible difference — each row is a unique product anyway

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
        Console.WriteLine(" WHY AsNoTrackingWithIdentityResolution EXISTS ");
        Console.WriteLine();
        Console.WriteLine("  Problem: AsNoTracking with Include() gives you N duplicate parent objects");
        Console.WriteLine("           for N child rows. Product 42 with 100 Reviews = 100 Product instances.");
        Console.WriteLine();
        Console.WriteLine("  Solution: ATNWIR keeps a temporary identity map for ONE query's lifetime.");
        Console.WriteLine("            Same parent key seen twice → same instance. Still untracked,");
        Console.WriteLine("            SaveChanges() still ignores it.");
        Console.WriteLine();
        Console.WriteLine("  Tradeoff:");
        Console.WriteLine("    + Fewer objects in memory for high fan-out joins.");
        Console.WriteLine("    - Extra allocation per query even when not needed.");
        Console.WriteLine("    → Use with Include(). Plain AsNoTracking() for flat reads.");
        Console.WriteLine("    → Never use either when you intend to save changes.");
    }

    private static void PrintHeader(string title)
    {
        Console.WriteLine();
        Console.WriteLine(new string('═', 65));
        Console.WriteLine($"  {title}");
        Console.WriteLine(new string('═', 65));
    }
}
