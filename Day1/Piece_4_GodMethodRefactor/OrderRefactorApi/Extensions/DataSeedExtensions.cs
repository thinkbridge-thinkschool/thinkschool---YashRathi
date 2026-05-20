using Microsoft.EntityFrameworkCore;
using OrderRefactorApi.Data;
using OrderRefactorApi.Models;

namespace OrderRefactorApi.Extensions;

public static class DataSeedExtensions
{
    public static async Task SeedDataAsync(this WebApplication app)
    {
        try
        {
            using var scope = app.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            await context.Database.EnsureCreatedAsync();

            if (await context.Customers.AnyAsync())
                return;

            var customers = new[]
            {
                new Customer { Id = 1, Type = CustomerType.Standard, IsDeleted = false, TotalOrderCount = 0, TotalSpent = 0, LastOrderDate = DateTime.MinValue, LoyaltyPoints = 0 },
                new Customer { Id = 2, Type = CustomerType.Premium, IsDeleted = false, TotalOrderCount = 0, TotalSpent = 0, LastOrderDate = DateTime.MinValue, LoyaltyPoints = 1500 },
                new Customer { Id = 3, Type = CustomerType.Gold, IsDeleted = false, TotalOrderCount = 0, TotalSpent = 0, LastOrderDate = DateTime.MinValue, LoyaltyPoints = 500 }
            };
            context.Customers.AddRange(customers);

            var products = new[]
            {
                new Product { Id = 1, Name = "Laptop", Price = 1000m, Category = ProductCategory.Electronics },
                new Product { Id = 2, Name = "Programming Book", Price = 50m, Category = ProductCategory.Books },
                new Product { Id = 3, Name = "T-Shirt", Price = 25m, Category = ProductCategory.Clothing },
                new Product { Id = 4, Name = "Mouse", Price = 30m, Category = ProductCategory.Electronics }
            };
            context.Products.AddRange(products);

            var inventories = new[]
            {
                new Inventory { ProductId = 1, QuantityOnHand = 50, LastModified = DateTime.UtcNow, ModifiedBy = "Seed" },
                new Inventory { ProductId = 2, QuantityOnHand = 200, LastModified = DateTime.UtcNow, ModifiedBy = "Seed" },
                new Inventory { ProductId = 3, QuantityOnHand = 500, LastModified = DateTime.UtcNow, ModifiedBy = "Seed" },
                new Inventory { ProductId = 4, QuantityOnHand = 300, LastModified = DateTime.UtcNow, ModifiedBy = "Seed" }
            };
            context.Inventory.AddRange(inventories);

            var discounts = new[]
            {
                new Discount { Id = 1, CustomerId = 2, Percentage = 5, Active = true },
                new Discount { Id = 2, CustomerId = 3, Percentage = 3, Active = true }
            };
            context.Discounts.AddRange(discounts);

            await context.SaveChangesAsync();
        }
        catch (InvalidOperationException)
        {
            // Skip seeding during integration tests when InMemory provider is used
        }
    }
}