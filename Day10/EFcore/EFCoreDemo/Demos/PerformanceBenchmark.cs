using System.Diagnostics;
using EFCoreDemo.Data;
using EFCoreDemo.Models;
using Microsoft.EntityFrameworkCore;

namespace EFCoreDemo.Demos;

public static class PerformanceBenchmark
{
    private const int WarmupRuns   = 2;
    private const int MeasuredRuns = 5;

    public static Task RunAsync(Func<AppDbContext> contextFactory)
    {
        PrintHeader("PART 4 — Performance Benchmark (10,000-row full read)");

        Console.WriteLine($"  Methodology:");
        Console.WriteLine($"    • {WarmupRuns} warm-up runs discarded (heats JIT + SQLite page cache)");
        Console.WriteLine($"    • {MeasuredRuns} measured runs per query type");
        Console.WriteLine($"    • Stopwatch for wall time; GC.GetAllocatedBytesForCurrentThread() for heap bytes");
        Console.WriteLine($"    • Fresh DbContext per run (tracked context carries snapshot per entity)");
        Console.WriteLine($"    • Synchronous queries so all allocation is on the same thread");
        Console.WriteLine($"    • Median of {MeasuredRuns} runs reported to suppress outliers");
        Console.WriteLine();

        // Warm-up (results discarded)
        Console.Write($"  Warming up ");
        for (int i = 0; i < WarmupRuns; i++)
        {
            RunTracked(contextFactory);
            RunAsNoTracking(contextFactory);
            Console.Write(".");
        }
        Console.WriteLine(" done.");
        Console.WriteLine();

        // Measured runs
        var trackedResults    = new List<(long ms, long bytes)>(MeasuredRuns);
        var noTrackingResults = new List<(long ms, long bytes)>(MeasuredRuns);

        for (int i = 0; i < MeasuredRuns; i++)
        {
            trackedResults.Add(RunTracked(contextFactory));
            noTrackingResults.Add(RunAsNoTracking(contextFactory));
        }

        PrintResults(trackedResults, noTrackingResults);
        return Task.CompletedTask;
    }

    // ── Core measurements ────────────────────────────────────────────────────

    private static (long ms, long bytes) RunTracked(Func<AppDbContext> contextFactory)
    {
        using var ctx = contextFactory();

        // Force GC so previous run's garbage doesn't inflate timing
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();

        var allocBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();

        List<Product> products = ctx.Products.ToList();   // synchronous — all on this thread

        sw.Stop();
        var allocAfter = GC.GetAllocatedBytesForCurrentThread();

        GC.KeepAlive(products); // prevent dead-code elimination
        return (sw.ElapsedMilliseconds, allocAfter - allocBefore);
    }

    private static (long ms, long bytes) RunAsNoTracking(Func<AppDbContext> contextFactory)
    {
        using var ctx = contextFactory();

        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();

        var allocBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();

        List<Product> products = ctx.Products.AsNoTracking().ToList();

        sw.Stop();
        var allocAfter = GC.GetAllocatedBytesForCurrentThread();

        GC.KeepAlive(products);
        return (sw.ElapsedMilliseconds, allocAfter - allocBefore);
    }

    // ── Result display ───────────────────────────────────────────────────────

    private static void PrintResults(
        List<(long ms, long bytes)> tracked,
        List<(long ms, long bytes)> noTracking)
    {
        long   tMs   = Median(tracked.Select(r => r.ms).ToArray());
        double tMB   = MedianD(tracked.Select(r => r.bytes / 1_048_576.0).ToArray());
        long   ntMs  = Median(noTracking.Select(r => r.ms).ToArray());
        double ntMB  = MedianD(noTracking.Select(r => r.bytes / 1_048_576.0).ToArray());

        double timeRatio = ntMs > 0 ? (double)tMs / ntMs : double.NaN;
        double memRatio  = ntMB > 0 ? tMB / ntMB         : double.NaN;

        Console.WriteLine("  ┌──────────────────────────────┬──────────────┬──────────────────┐");
        Console.WriteLine("  │ Query Type                   │  Time (ms)   │  Allocated (MB)  │");
        Console.WriteLine("  ├──────────────────────────────┼──────────────┼──────────────────┤");
        Console.WriteLine($"  │ Tracked (default)            │  {tMs,8} ms │  {tMB,10:F2} MB  │");
        Console.WriteLine($"  │ AsNoTracking                 │  {ntMs,8} ms │  {ntMB,10:F2} MB  │");
        Console.WriteLine("  ├──────────────────────────────┼──────────────┼──────────────────┤");
        Console.WriteLine($"  │ Ratio (tracked / no-track)   │  {timeRatio,9:F2}x │  {memRatio,12:F2}x  │");
        Console.WriteLine("  └──────────────────────────────┴──────────────┴──────────────────┘");
        Console.WriteLine();

        Console.WriteLine("  Raw runs — Tracked    : " + string.Join("  ", tracked.Select(r    => $"{r.ms,4}ms/{r.bytes / 1_048_576.0:F1}MB")));
        Console.WriteLine("  Raw runs — AsNoTracking: " + string.Join("  ", noTracking.Select(r => $"{r.ms,4}ms/{r.bytes / 1_048_576.0:F1}MB")));
        Console.WriteLine();
        Console.WriteLine("  WHAT THE NUMBERS MEAN:");
        Console.WriteLine("    Memory delta  The ChangeTracker stores an original-value snapshot");
        Console.WriteLine("                  (object[] of boxed primitives) for every tracked entity.");
        Console.WriteLine("                  For 10k Product rows this adds ~200–400 bytes per entity");
        Console.WriteLine("                  on top of the entity objects themselves.");
        Console.WriteLine();
        Console.WriteLine("    Time delta    Tracking overhead comes from:");
        Console.WriteLine("                    1. Dictionary<IKey,object> lookup per materialized row");
        Console.WriteLine("                    2. Snapshot boxing (Price, CreatedAt → heap-boxed object[])");
        Console.WriteLine("                    3. EntityEntry wrapper allocation per entity");
        Console.WriteLine("                    4. DetectChanges() scan on SaveChanges()");
        Console.WriteLine();
        Console.WriteLine("    Caveat        I/O cost (SQLite read from OS page cache) is IDENTICAL");
        Console.WriteLine("                  for both queries. The measured delta is pure EF Core");
        Console.WriteLine("                  materialization + tracking bookkeeping overhead.");
        Console.WriteLine("                  On a remote SQL Server the I/O cost would dominate and");
        Console.WriteLine("                  the relative difference would look smaller — but the");
        Console.WriteLine("                  absolute memory overhead is the same regardless of server.");
    }

    private static long Median(long[] arr)
    {
        Array.Sort(arr);
        return arr[arr.Length / 2];
    }

    private static double MedianD(double[] arr)
    {
        Array.Sort(arr);
        return arr[arr.Length / 2];
    }

    private static void PrintHeader(string title)
    {
        Console.WriteLine();
        Console.WriteLine(new string('═', 65));
        Console.WriteLine($"  {title}");
        Console.WriteLine(new string('═', 65));
    }
}
