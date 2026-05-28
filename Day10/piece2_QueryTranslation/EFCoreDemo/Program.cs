using EFCoreDemo.Data;
using EFCoreDemo.Demos;
using EFCoreDemo.Seeder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

// Bootstrap

var dbPath = Path.Combine(AppContext.BaseDirectory, "efcore_demo.db");
var connectionString = $"Data Source={dbPath}";

AppDbContext CreateContext() =>
    new(new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite(connectionString)
        .EnableSensitiveDataLogging(false)   // keep SQL logs quiet during demo
        .Options);

// Logging context: captures SQL statements into a caller-supplied list.
// EnableSensitiveDataLogging shows parameter values; LogLevel.Information
// includes the CommandExecuted event that carries the final SQL.
AppDbContext CreateLoggingContext(List<string> sqlLog) =>
    new(new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite(connectionString)
        .EnableSensitiveDataLogging(true)
        .LogTo(msg => sqlLog.Add(msg), LogLevel.Information)
        .Options);


//Database setup & seeding 

using (var ctx = CreateContext())
{
    await ctx.Database.EnsureCreatedAsync();
    await DataSeeder.SeedAsync(ctx);
}

Console.WriteLine();

//  Run all demo sections

await ChangeTrackingDemo.RunAsync(CreateContext);
await IdentityResolutionDemo.RunAsync(CreateContext);
await PerformanceBenchmark.RunAsync(CreateContext);
await EdgeCasesDemo.RunAsync(CreateContext);
await QueryTranslationDemo.RunAsync(CreateLoggingContext);

// Footer 

Console.WriteLine();
Console.WriteLine("  All demos complete. Run tests:  dotnet test ../EFCoreDemo.Tests");
