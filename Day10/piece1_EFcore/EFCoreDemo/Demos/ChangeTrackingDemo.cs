using EFCoreDemo.Data;
using EFCoreDemo.Models;
using Microsoft.EntityFrameworkCore;

namespace EFCoreDemo.Demos;

public static class ChangeTrackingDemo
{
    public static async Task RunAsync(Func<AppDbContext> contextFactory)
    {
        PrintHeader("PART 1 — Change Tracking Demo");

        await RunTrackedUpdate(contextFactory);
        Console.WriteLine();
        await RunAsNoTrackingNoUpdate(contextFactory);
    }

    // Scenario A

    private static async Task RunTrackedUpdate(Func<AppDbContext> contextFactory)
    {
        Console.WriteLine("SCENARIO A: Tracked query → modify → SaveChanges");

        int    targetId;
        decimal originalPrice;
        decimal newPrice;

        using (var ctx = contextFactory())
        {
            var product = await ctx.Products.FirstAsync();
            targetId      = product.Id;
            originalPrice = product.Price;
            newPrice      = Math.Round(originalPrice + 100m, 2);

            var entry = ctx.Entry(product);

            Console.WriteLine($"  [Query]  Id={targetId}, Name='{product.Name}', Price={originalPrice:F2}");
            Console.WriteLine($"  [State]  EntityState right after query   : {entry.State}");

            product.Price = newPrice;

            Console.WriteLine($"  [Modify] Price changed in-memory              : {originalPrice:F2} → {newPrice:F2}");
            Console.WriteLine($"  [State]  EntityState BEFORE DetectChanges()   : {entry.State}  ← lazy — snapshot not compared yet");

            // snapshot tracking is lazy — state doesn't flip until DetectChanges() runs
            ctx.ChangeTracker.DetectChanges();

            Console.WriteLine($"  [State]  EntityState AFTER  DetectChanges()   : {entry.State}");
            Console.WriteLine($"  [Track]  Modified properties                   : {string.Join(", ", entry.Properties.Where(p => p.IsModified).Select(p => p.Metadata.Name))}");

            int rows = await ctx.SaveChangesAsync();
            Console.WriteLine($"  [Save]   SaveChanges() rows affected     : {rows}");
        }

        using (var freshCtx = contextFactory())
        {
            var verified = await freshCtx.Products.FindAsync(targetId);
            bool persisted = verified!.Price == newPrice;

            Console.WriteLine($"  [Verify] Price in DB (new context)       : {verified.Price:F2}");
            Console.WriteLine($"  [Result] UPDATE PERSISTED                : {(persisted ? "YES ✓" : "NO ✗ — UNEXPECTED")}");
        }
    }

    // Scenario B

    private static async Task RunAsNoTrackingNoUpdate(Func<AppDbContext> contextFactory)
    {

        Console.WriteLine("SCENARIO B: AsNoTracking query → modify → SaveChanges");

        int     targetId;
        decimal originalPrice;
        decimal attemptedPrice;

        using (var ctx = contextFactory())
        {
            var product = await ctx.Products.AsNoTracking().FirstAsync();
            targetId       = product.Id;
            originalPrice  = product.Price;
            attemptedPrice = Math.Round(originalPrice + 555m, 2);

            bool inTracker = ctx.ChangeTracker.Entries<Product>().Any(e => e.Entity.Id == product.Id);

            Console.WriteLine($"  [Query]  Id={targetId}, Name='{product.Name}', Price={originalPrice:F2}");
            Console.WriteLine($"  [Track]  Is entity in ChangeTracker      : {inTracker}  (expected: False)");

            product.Price = attemptedPrice;
            Console.WriteLine($"  [Modify] Price changed in-memory         : {originalPrice:F2} → {attemptedPrice:F2}");

            int rows = await ctx.SaveChangesAsync();
            Console.WriteLine($"  [Save]   SaveChanges() rows affected     : {rows}  (expected: 0)");
            Console.WriteLine($"           ^ nothing was tracked — no SQL generated.");
        }

        using (var freshCtx = contextFactory())
        {
            var verified  = await freshCtx.Products.FindAsync(targetId);
            bool unchanged = verified!.Price == originalPrice;

            Console.WriteLine($"  [Verify] Price in DB (new context)       : {verified.Price:F2}");
            Console.WriteLine($"  [Result] DB UNCHANGED (no stale write)   : {(unchanged ? "YES ✓" : "NO ✗ — UNEXPECTED")}");
        }
    }

    private static void PrintHeader(string title)
    {
        Console.WriteLine();
        Console.WriteLine(new string('═', 65));
        Console.WriteLine($"  {title}");
        Console.WriteLine(new string('═', 65));
    }
}
