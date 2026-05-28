using EFCoreDemo.Data;
using EFCoreDemo.Models;
using Microsoft.EntityFrameworkCore;

namespace EFCoreDemo.Demos;

public static class EdgeCasesDemo
{
    public static async Task RunAsync(Func<AppDbContext> contextFactory)
    {
        PrintHeader("PART 4 — Edge Cases & Production Failure Modes");

        await EdgeCase1_SilentNoOpAfterAsNoTracking(contextFactory);
        Console.WriteLine();
        await EdgeCase2_TrackerBloatWithLargeQuery(contextFactory);
        Console.WriteLine();
        await EdgeCase3_LongLivedContextAccumulation(contextFactory);
        Console.WriteLine();
        EdgeCase4_IdentityResolutionMemoryTradeoff();
        Console.WriteLine();
        await EdgeCase5_MixingTrackedAndUntracked(contextFactory);
        Console.WriteLine();
        await EdgeCase6_DuplicateKeyThrowsException(contextFactory);
    }

    //EC-1: Silent no-op after AsNoTracking modification

    private static async Task EdgeCase1_SilentNoOpAfterAsNoTracking(Func<AppDbContext> contextFactory)
    {

        Console.WriteLine("EC-1  AsNoTracking update is a SILENT no-op — no exception");

        using var ctx = contextFactory();

        var product = await ctx.Products.AsNoTracking().FirstAsync();
        decimal priceInMemoryBefore = product.Price;

        product.Price += 9_999m;           // mutate in-memory — developer thinks this will save
        int rows = await ctx.SaveChangesAsync();

        Console.WriteLine($"  product.Price in memory : {product.Price:F2}  (object was mutated)");
        Console.WriteLine($"  SaveChanges() rows      : {rows}  ← EF Core generated ZERO SQL");
        Console.WriteLine($"  Original price in DB    : {priceInMemoryBefore:F2}  (unchanged)");
        Console.WriteLine();
        Console.WriteLine("  PRODUCTION IMPACT:");
        Console.WriteLine("    • No exception is thrown. No warning is logged.");
        Console.WriteLine("    • The caller receives a 200 OK response with stale data in the DB.");
        Console.WriteLine("    • The bug surfaces only when users report that changes aren't saved.");
        Console.WriteLine("    • Hardest to reproduce: the error is in a repository method that");
        Console.WriteLine("      conditionally applies AsNoTracking — the caller has no way to know.");
        Console.WriteLine("  FIX: Never use AsNoTracking() in code paths that modify + save.");
    }

    // EC-2: Tracker bloat with a large tracked query 

    private static async Task EdgeCase2_TrackerBloatWithLargeQuery(Func<AppDbContext> contextFactory)
    {
        Console.WriteLine("EC-2  Tracked read of all 10k rows inflates memory");

        using var ctx = contextFactory();

        long memBefore = GC.GetTotalMemory(forceFullCollection: true);

        var products = await ctx.Products.ToListAsync();    // 10k tracked entities

        long memAfter     = GC.GetTotalMemory(forceFullCollection: false);
        int  trackerCount = ctx.ChangeTracker.Entries<Product>().Count();

        Console.WriteLine($"  Entities loaded          : {products.Count:N0}");
        Console.WriteLine($"  ChangeTracker.Entries    : {trackerCount:N0}  (one entry per entity)");
        Console.WriteLine($"  GC.GetTotalMemory delta  : ~{(memAfter - memBefore) / 1024:N0} KB");
        Console.WriteLine();
        Console.WriteLine("  Each entry holds the entity + a snapshot of all original values.");
        Console.WriteLine();
        Console.WriteLine("  PRODUCTION IMPACT:");
        Console.WriteLine("    A background reporting job that does context.Products.ToList()");
        Console.WriteLine("    for a 100k-row table will hold ~40-80 MB of tracker data that");
        Console.WriteLine("    is useless if no update follows. On a server with 512 MB RAM");
        Console.WriteLine("    and 20 concurrent workers, this exhausts memory.");
        Console.WriteLine("  FIX: Use AsNoTracking() for all read-only operations (reports, APIs).");

        GC.KeepAlive(products);
    }

    // EC-3: Long-lived context accumulates tracked entities 

    private static async Task EdgeCase3_LongLivedContextAccumulation(Func<AppDbContext> contextFactory)
    {
        Console.WriteLine("EC-3  Long-lived DbContext — tracker grows with every query ");

        using var ctx = contextFactory();   // simulates a Singleton or reused DbContext

        Console.WriteLine("  Loading 200 products per 'request' into the same DbContext:");

        for (int batch = 1; batch <= 5; batch++)
        {
            _ = await ctx.Products
                .OrderBy(p => p.Id)
                .Skip((batch - 1) * 200)
                .Take(200)
                .ToListAsync();

            int count = ctx.ChangeTracker.Entries<Product>().Count();
            Console.WriteLine($"    After request {batch}: ChangeTracker.Entries = {count,5}  (+200 per call)");
        }

        Console.WriteLine();
        Console.WriteLine("  PRODUCTION IMPACT:");
        Console.WriteLine("    In ASP.NET Core with a SINGLETON DbContext (a common misuse),");
        Console.WriteLine("    every API request loads more rows into the same context.");
        Console.WriteLine("    The tracker never shrinks until the process restarts.");
        Console.WriteLine("    DetectChanges() on SaveChanges() scans ALL tracked entries —");
        Console.WriteLine("    the more entries, the slower every future SaveChanges() call.");
        Console.WriteLine("  FIX 1: Always register DbContext as SCOPED (one per HTTP request).");
        Console.WriteLine("  FIX 2: Call context.ChangeTracker.Clear() between logical units.");
        Console.WriteLine("  FIX 3: Use AsNoTracking() so entries never accumulate.");
    }

    //  EC-4: ATNWIR memory tradeoff (static analysis — no DB needed) 

    private static void EdgeCase4_IdentityResolutionMemoryTradeoff()
    {
        Console.WriteLine("EC-4  AsNoTrackingWithIdentityResolution: overhead for flat queries vs. benefit for Include(1:N) queries");
        Console.WriteLine();
        Console.WriteLine("  Query scenario A — FLAT (single table, no navigation):");
        Console.WriteLine("    context.Products.AsNoTracking().ToList()      → best choice");
        Console.WriteLine("    context.Products.AsNoTrackingWithIdentityResolution().ToList()");
        Console.WriteLine("      → same result, but extra allocation per query call.");
        Console.WriteLine("        No entity appears twice in a flat query — pure overhead.");
        Console.WriteLine();
        Console.WriteLine("  Query scenario B — JOIN (Include with 1:N navigation):");
        Console.WriteLine("    context.Orders.AsNoTracking().Include(o => o.Product).ToList()");
        Console.WriteLine("      → Product with Id=5 has 50 orders → 50 Product objects in RAM.");
        Console.WriteLine("    context.Orders.AsNoTrackingWithIdentityResolution()");
        Console.WriteLine("                  .Include(o => o.Product).ToList()");
        Console.WriteLine("      → Product with Id=5 has 50 orders → 1 Product object in RAM.");
        Console.WriteLine("        Memory saving: 49 × sizeof(Product).");
        Console.WriteLine();
        Console.WriteLine("  Rule: Use ATNWIR only when the same parent appears many times in one query.");
    }

    // EC-5: Mixing tracked and untracked entities

    private static async Task EdgeCase5_MixingTrackedAndUntracked(Func<AppDbContext> contextFactory)
    {
        Console.WriteLine("EC-5  Accidentally mixing tracked and untracked entities");

        using var ctx = contextFactory();

        var tracked   = await ctx.Products.FirstAsync();
        var untracked = await ctx.Products.AsNoTracking().OrderByDescending(p => p.Id).FirstAsync();

        bool trackedInCtx   = ctx.ChangeTracker.Entries<Product>().Any(e => e.Entity.Id == tracked.Id);
        bool untrackedInCtx = ctx.ChangeTracker.Entries<Product>().Any(e => e.Entity.Id == untracked.Id);

        Console.WriteLine($"  tracked   Id={tracked.Id,-6}  InChangeTracker: {trackedInCtx}");
        Console.WriteLine($"  untracked Id={untracked.Id,-6}  InChangeTracker: {untrackedInCtx}");

        // Modify both, save — only the tracked one persists
        decimal originalTracked   = tracked.Price;
        decimal originalUntracked = untracked.Price;

        tracked.Price   = 1.11m;
        untracked.Price = 2.22m;

        int rows = await ctx.SaveChangesAsync();

        Console.WriteLine($"  After modifying both and SaveChanges(): rows affected = {rows}");
        Console.WriteLine($"  tracked.Price saved    : {tracked.Price:F2}  (expected: 1.11)");
        Console.WriteLine($"  untracked.Price in DB  : {originalUntracked:F2}  (unchanged, expected)");
        Console.WriteLine();
        Console.WriteLine("  PRODUCTION IMPACT:");
        Console.WriteLine("    A service receives a Product from a repository. The repository");
        Console.WriteLine("    returns AsNoTracking on GETs and tracked on POSTs. The caller");
        Console.WriteLine("    code path treats them identically — compiles, runs, no crash,");
        Console.WriteLine("    but 50% of updates silently fail depending on which code path ran.");
        Console.WriteLine("  FIX: Repository interfaces should document tracking behavior. Consider");
        Console.WriteLine("       separate method names: FindForRead() vs FindForUpdate().");
    }

    // ── EC-6: Duplicate tracked entity key throws InvalidOperationException ──

    private static async Task EdgeCase6_DuplicateKeyThrowsException(Func<AppDbContext> contextFactory)
    {
        Console.WriteLine("EC-6  Adding an entity with a key already tracked throws");

        using var ctx = contextFactory();

        int id = await ctx.Products.Select(p => p.Id).FirstAsync();
        var alreadyTracked = await ctx.Products.FindAsync(id);  // tracked

        var detachedDuplicate = new Product
        {
            Id        = id,
            Name      = "Duplicate Object",
            Price     = 1m,
            Category  = "X",
            CreatedAt = DateTime.UtcNow
        };

        Console.WriteLine($"  Product Id={id} is tracked: {ctx.ChangeTracker.Entries<Product>().Any(e => e.Entity.Id == id)}");
        Console.WriteLine($"  Attempting ctx.Products.Add(new Product {{ Id={id} }}) ...");

        try
        {
            ctx.Products.Add(detachedDuplicate);
            Console.WriteLine("  ERROR: Expected exception was NOT thrown.");
        }
        catch (InvalidOperationException ex)
        {
            string short_ = ex.Message.Length > 130 ? ex.Message[..130] + "..." : ex.Message;
            Console.WriteLine($"  EXCEPTION (expected): {short_}");
        }

        Console.WriteLine();
        Console.WriteLine("  PRODUCTION IMPACT:");
        Console.WriteLine("    Arises in PUT/PATCH handlers that: (1) load an entity for validation,");
        Console.WriteLine("    (2) map a DTO to a new entity object, (3) call Add() to 'save' it.");
        Console.WriteLine("    The entity from step 1 is still tracked — step 3 collides.");
        Console.WriteLine("  FIX: Use ctx.Entry(mapped).State = EntityState.Modified for detached");
        Console.WriteLine("       updates, OR call ctx.ChangeTracker.Clear() before re-attaching.");

        GC.KeepAlive(alreadyTracked);
    }

    private static void PrintHeader(string title)
    {
        Console.WriteLine();
        Console.WriteLine(new string('═', 65));
        Console.WriteLine($"  {title}");
        Console.WriteLine(new string('═', 65));
    }
}
