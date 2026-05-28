using EFCoreDemo.Data;
using EFCoreDemo.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EFCoreDemo.Tests;


public sealed class ChangeTrackingTests : IDisposable
{
    private readonly SqliteConnection             _conn;
    private readonly DbContextOptions<AppDbContext> _opts;

    public ChangeTrackingTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();

        _opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_conn)
            .Options;

        using var ctx = NewContext();
        ctx.Database.EnsureCreated();
        SeedFixture(ctx);
    }

    public void Dispose() => _conn.Dispose();

    private AppDbContext NewContext() => new(_opts);

    private static void SeedFixture(AppDbContext ctx)
    {
        ctx.Products.AddRange(
            new Product { Name = "Widget Alpha",   Price = 100.00m, Category = "Tools", CreatedAt = DateTime.UtcNow },
            new Product { Name = "Widget Beta",    Price = 200.00m, Category = "Tools", CreatedAt = DateTime.UtcNow },
            new Product { Name = "Widget Gamma",   Price = 300.00m, Category = "Toys",  CreatedAt = DateTime.UtcNow }
        );
        ctx.SaveChanges();
    }
    // GROUP 1 — Tracked entity state transitions

    [Fact]
    public async Task Tracked_EntityState_IsUnchanged_RightAfterQuery()
    {
        using var ctx = NewContext();
        var product = await ctx.Products.FirstAsync();

        Assert.Equal(EntityState.Unchanged, ctx.Entry(product).State);
    }

    [Fact]
    public async Task Tracked_EntityState_BecomesModified_AfterPropertyChange()
    {
        using var ctx = NewContext();
        var product = await ctx.Products.FirstAsync();
        product.Price += 1m;

        Assert.Equal(EntityState.Modified, ctx.Entry(product).State);
    }

    [Fact]
    public async Task Tracked_EntityState_IsStillUnchanged_BeforeDetectChanges_ThenModifiedAfter()
    {
        // Proves change detection is LAZY: EF does not flip state on mutation.
        // State stays Unchanged until DetectChanges() (or SaveChanges()) compares snapshots.
        using var ctx = NewContext();
        ctx.ChangeTracker.AutoDetectChangesEnabled = false;

        var product = await ctx.Products.FirstAsync();
        product.Price += 1m;

        // Before explicit DetectChanges — still Unchanged
        var entryBeforeDetect = ctx.ChangeTracker.Entries<Product>()
            .First(e => e.Entity.Id == product.Id);
        Assert.Equal(EntityState.Unchanged, entryBeforeDetect.State);

        ctx.ChangeTracker.DetectChanges();

        // After DetectChanges — now Modified
        Assert.Equal(EntityState.Modified, entryBeforeDetect.State);
    }

    [Fact]
    public async Task Tracked_SaveChanges_ReturnsOneRowAffected_OnSingleModification()
    {
        using var ctx = NewContext();
        var product = await ctx.Products.FirstAsync();
        product.Price = 999m;

        int rows = await ctx.SaveChangesAsync();

        Assert.Equal(1, rows);
    }

    // GROUP 2 — Tracked update persists to the actual database
    //           Verified by reading back in a SECOND context so we prove
    //           the change is on disk, not just in the first context's cache.
    [Fact]
    public async Task Tracked_PriceChange_PersistedToDB_ConfirmedBySecondContext()
    {
        int id;

        // Act: modify and save in context 1
        using (var ctx = NewContext())
        {
            var product = await ctx.Products.FirstAsync();
            id = product.Id;
            product.Price = 888.88m;
            await ctx.SaveChangesAsync();
        }

        // Assert: read from a brand-new context (no in-memory cache involved)
        using (var verifyCtx = NewContext())
        {
            var saved = await verifyCtx.Products.FindAsync(id);
            Assert.NotNull(saved);
            Assert.Equal(888.88m, saved.Price);
        }
    }

    [Fact]
    public async Task Tracked_NameChange_PersistedToDB_ConfirmedBySecondContext()
    {
        int id;
        const string newName = "Renamed Widget";

        using (var ctx = NewContext())
        {
            var product = await ctx.Products.FirstAsync();
            id = product.Id;
            product.Name = newName;
            await ctx.SaveChangesAsync();
        }

        using (var verifyCtx = NewContext())
        {
            var saved = await verifyCtx.Products.FindAsync(id);
            Assert.Equal(newName, saved!.Name);
        }
    }

    // GROUP 3 — AsNoTracking: modifications do NOT reach the database


    [Fact]
    public async Task AsNoTracking_EntityIsNotInChangeTracker()
    {
        using var ctx = NewContext();
        var product = await ctx.Products.AsNoTracking().FirstAsync();

        bool inTracker = ctx.ChangeTracker.Entries<Product>().Any(e => e.Entity.Id == product.Id);

        Assert.False(inTracker);
    }

    [Fact]
    public async Task AsNoTracking_SaveChanges_ReturnsZeroRows()
    {
        using var ctx = NewContext();
        var product = await ctx.Products.AsNoTracking().FirstAsync();
        product.Price = 777m;

        int rows = await ctx.SaveChangesAsync();

        Assert.Equal(0, rows);
    }

    [Fact]
    public async Task AsNoTracking_PriceChange_DoesNotPersistToDB_ConfirmedBySecondContext()
    {
        decimal originalPrice;
        int id;

        // Capture original price from DB
        using (var captureCtx = NewContext())
        {
            var p = await captureCtx.Products.AsNoTracking().FirstAsync();
            id = p.Id;
            originalPrice = p.Price;
        }

        // Attempt a "save" via AsNoTracking path
        using (var ctx = NewContext())
        {
            var product = await ctx.Products.AsNoTracking().FirstAsync(p => p.Id == id);
            product.Price = originalPrice + 9_999m;
            await ctx.SaveChangesAsync();   // generates no SQL — nothing tracked
        }

        // Verify DB is unchanged
        using (var verifyCtx = NewContext())
        {
            var unchanged = await verifyCtx.Products.FindAsync(id);
            Assert.Equal(originalPrice, unchanged!.Price);
        }
    }

    [Fact]
    public async Task AsNoTracking_MultipleModifications_NonePersistedToDB()
    {
        // Captures all three original prices, mutates all three untracked, verifies none changed.
        var originals = new Dictionary<int, decimal>();

        using (var ctx = NewContext())
        {
            var all = await ctx.Products.AsNoTracking().ToListAsync();
            foreach (var p in all) originals[p.Id] = p.Price;

            foreach (var p in all) p.Price = 0.01m;
            await ctx.SaveChangesAsync();
        }

        using (var verifyCtx = NewContext())
        {
            var all = await verifyCtx.Products.AsNoTracking().ToListAsync();
            foreach (var p in all)
            {
                Assert.True(originals.ContainsKey(p.Id), $"Id {p.Id} missing from originals");
                Assert.Equal(originals[p.Id], p.Price);
            }
        }
    }

    // GROUP 4 — Identity resolution

    [Fact]
    public async Task Tracked_SameId_QueriedTwice_ReturnsSameReference()
    {
        using var ctx = NewContext();
        int id = await ctx.Products.Select(p => p.Id).FirstAsync();

        var first  = await ctx.Products.FirstAsync(p => p.Id == id);
        var second = await ctx.Products.FirstAsync(p => p.Id == id);

        Assert.True(ReferenceEquals(first, second),
            "ChangeTracker identity map must return the same C# instance for the same primary key.");
    }

    [Fact]
    public async Task AsNoTracking_SameId_QueriedTwice_ReturnsDifferentReferences()
    {
        using var ctx = NewContext();
        int id = await ctx.Products.Select(p => p.Id).FirstAsync();

        var first  = await ctx.Products.AsNoTracking().FirstAsync(p => p.Id == id);
        var second = await ctx.Products.AsNoTracking().FirstAsync(p => p.Id == id);

        Assert.False(ReferenceEquals(first, second),
            "AsNoTracking queries must allocate a new object on every call — no identity map.");
    }

    [Fact]
    public async Task AsNoTrackingWithIdentityResolution_SeparateQueries_ReturnDifferentReferences()
    {
        // ATNWIR identity map is scoped to a single query, NOT to the context lifetime.
        // Two separate query calls each have their own transient map → different instances.
        using var ctx = NewContext();
        int id = await ctx.Products.Select(p => p.Id).FirstAsync();

        var first  = await ctx.Products.AsNoTrackingWithIdentityResolution().FirstAsync(p => p.Id == id);
        var second = await ctx.Products.AsNoTrackingWithIdentityResolution().FirstAsync(p => p.Id == id);

        Assert.False(ReferenceEquals(first, second),
            "ATNWIR identity map is per-query-scope, not per-context. Separate queries → separate instances.");
    }

    // GROUP 5 — ChangeTracker utility operations

    [Fact]
    public async Task ChangeTracker_Clear_DetachesAllTrackedEntities()
    {
        using var ctx = NewContext();
        await ctx.Products.ToListAsync();

        Assert.Equal(3, ctx.ChangeTracker.Entries<Product>().Count());

        ctx.ChangeTracker.Clear();

        Assert.Empty(ctx.ChangeTracker.Entries<Product>());
    }

    [Fact]
    public async Task ChangeTracker_Clear_ThenModify_ChangesAreNotSaved()
    {
        // After Clear(), previously loaded entities are detached.
        // Modifying them and saving should produce 0 rows.
        using var ctx = NewContext();
        var products = await ctx.Products.ToListAsync();   // tracked

        ctx.ChangeTracker.Clear();   // detach everything

        foreach (var p in products) p.Price = 0.01m;

        int rows = await ctx.SaveChangesAsync();
        Assert.Equal(0, rows);
    }

    // GROUP 6 — Edge case: duplicate key throws when already tracked


    [Fact]
    public async Task AddingDuplicateKey_WhenAlreadyTracked_ThrowsInvalidOperationException()
    {
        using var ctx = NewContext();
        int id = await ctx.Products.Select(p => p.Id).FirstAsync();

        await ctx.Products.FindAsync(id);   // now tracked in context

        var duplicate = new Product { Id = id, Name = "Dup", Price = 1m, Category = "X", CreatedAt = DateTime.UtcNow };

        Assert.Throws<InvalidOperationException>(() => ctx.Products.Add(duplicate));
    }

    [Fact]
    public async Task UsingEntryStateModified_IsCorrectWayToUpdateDetachedEntity()
    {
        int id;
        decimal originalPrice;

        using (var ctx = NewContext())
        {
            var p = await ctx.Products.AsNoTracking().FirstAsync();
            id            = p.Id;
            originalPrice = p.Price;
        }

        const decimal updatedPrice = 42.42m;

        // Correct pattern for updating a detached entity without loading it first
        using (var ctx = NewContext())
        {
            var detached = new Product { Id = id, Name = "Updated Name", Price = updatedPrice, Category = "Tools", CreatedAt = DateTime.UtcNow };
            ctx.Entry(detached).State = EntityState.Modified;
            await ctx.SaveChangesAsync();
        }

        using (var verifyCtx = NewContext())
        {
            var saved = await verifyCtx.Products.FindAsync(id);
            Assert.Equal(updatedPrice, saved!.Price);
        }
    }
}
