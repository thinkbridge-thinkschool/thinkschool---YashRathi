using Microsoft.EntityFrameworkCore;
using QuotesApi.Models;

namespace QuotesApi.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options) { }

    public DbSet<Quote> Quotes => Set<Quote>();
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(b =>
        {
            b.HasIndex(u => u.Email).IsUnique();
        });

        modelBuilder.Entity<RefreshToken>(b =>
        {
            b.HasIndex(r => r.TokenHash).IsUnique();
            b.HasIndex(r => r.FamilyId);
            b.HasOne(r => r.User)
             .WithMany()
             .HasForeignKey(r => r.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // Migrations were scaffolded with the SQLite provider, which maps DateTimeOffset
        // to TEXT. SQL Server accepts TEXT as a column type (deprecated char LOB) but
        // rejects inserting datetimeoffset values into it. Override to the native type
        // when running against SQL Server so migrations create the correct column types.
        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.SqlServer")
        {
            modelBuilder.Entity<Quote>()
                .Property(q => q.CreatedAt)
                .HasColumnType("datetimeoffset");

            modelBuilder.Entity<RefreshToken>(b =>
            {
                b.Property(r => r.ExpiresAt).HasColumnType("datetimeoffset");
                b.Property(r => r.RevokedAt).HasColumnType("datetimeoffset");
            });
        }
    }
}
