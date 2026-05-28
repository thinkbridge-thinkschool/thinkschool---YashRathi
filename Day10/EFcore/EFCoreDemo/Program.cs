using EFCoreDemo.Data;
using EFCoreDemo.Demos;
using EFCoreDemo.Seeder;
using Microsoft.EntityFrameworkCore;

// ── Bootstrap ───────────────────────────────────────────────────────────────

var dbPath = Path.Combine(AppContext.BaseDirectory, "efcore_demo.db");
var connectionString = $"Data Source={dbPath}";

AppDbContext CreateContext() =>
    new(new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite(connectionString)
        .EnableSensitiveDataLogging(false)   // keep SQL logs quiet during demo
        .Options);

Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
Console.WriteLine("║     EF Core Change Tracker + AsNoTracking — Complete Demo        ║");
Console.WriteLine("║     ThinkSchool Day10 · Piece 1                                  ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
Console.WriteLine();

// ── Database setup & seeding ─────────────────────────────────────────────────

using (var ctx = CreateContext())
{
    await ctx.Database.EnsureCreatedAsync();
    await DataSeeder.SeedAsync(ctx);
}

Console.WriteLine();

// ── Run all demo sections ────────────────────────────────────────────────────

await ChangeTrackingDemo.RunAsync(CreateContext);
await IdentityResolutionDemo.RunAsync(CreateContext);
await PerformanceBenchmark.RunAsync(CreateContext);
await EdgeCasesDemo.RunAsync(CreateContext);

// ── Footer ───────────────────────────────────────────────────────────────────

Console.WriteLine();
Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
Console.WriteLine("║  All demos complete.                                              ║");
Console.WriteLine("║  Run tests:  dotnet test ../EFCoreDemo.Tests                      ║");
Console.WriteLine("║  Reflection: see Reflection.md at repo root                      ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
