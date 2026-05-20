using Microsoft.EntityFrameworkCore;
using OrderRefactorApi.Models;

namespace OrderRefactorApi.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Inventory> Inventory => Set<Inventory>();
    public DbSet<Discount> Discounts => Set<Discount>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure Order -> OrderItems as owned collection
        modelBuilder.Entity<Order>()
            .OwnsMany(o => o.Items, oi =>
            {
                oi.WithOwner().HasForeignKey("OrderId");
                oi.Property<int>("Id");
                oi.HasKey("Id");
            });

        // Inventory primary key is Id
        modelBuilder.Entity<Inventory>()
            .HasKey(i => i.Id);
    }
}
