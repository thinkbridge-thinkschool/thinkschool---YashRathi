using EFCoreDemo.Data;
using EFCoreDemo.Models;
using Microsoft.EntityFrameworkCore;

namespace EFCoreDemo.Seeder;

public static class DataSeeder
{
    private static readonly string[] Categories =
        ["Electronics", "Clothing", "Books", "Food", "Sports", "Home", "Beauty", "Toys", "Automotive", "Garden"];

    private static readonly string[] Adjectives =
        ["Premium", "Budget", "Pro", "Elite", "Classic", "Modern", "Vintage", "Smart", "Ultra", "Eco"];

    private static readonly string[] Nouns =
        ["Gadget", "Widget", "Tool", "Device", "Kit", "Pack", "Bundle", "Set", "Item", "Unit"];

    public static async Task SeedAsync(AppDbContext context)
    {
        if (await context.Products.AnyAsync())
        {
            Console.WriteLine("[Seeder] Database already seeded — skipping.");
            return;
        }

        Console.Write("[Seeder] Inserting 10,000 products in batches of 1,000 ... ");

        var rng = new Random(42);
        var batch = new List<Product>(1000);

        for (int i = 1; i <= 10_000; i++)
        {
            batch.Add(new Product
            {
                Name     = $"{Adjectives[rng.Next(Adjectives.Length)]} {Nouns[rng.Next(Nouns.Length)]} #{i:D5}",
                Price    = Math.Round((decimal)(rng.NextDouble() * 999 + 0.99), 2),
                Category = Categories[rng.Next(Categories.Length)],
                CreatedAt = DateTime.UtcNow.AddDays(-rng.Next(730))
            });

            if (i % 1000 == 0)
            {
                await context.Products.AddRangeAsync(batch);
                await context.SaveChangesAsync();
                // Clear tracker between batches to avoid memory bloat during seeding
                context.ChangeTracker.Clear();
                batch.Clear();
                Console.Write($"{i / 1000}k ");
            }
        }

        Console.WriteLine("done.");
    }
}
