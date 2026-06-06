using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Abstractions;
using QuotesApi.Commands;
using QuotesApi.Data;
using QuotesApi.Queries;
using QuotesApi.ReadModels;
using QuotesApi.Repositories;
using Xunit;

namespace QuotesApi.Tests;

/// <summary>
/// Verifies CQRS-lite handlers in isolation using a real SQLite DB context.
///
/// Write path  — CreateQuoteCommandHandler: validates input, persists, returns the new ID.
/// Read path   — GetQuotesQueryHandler / GetQuoteByIdQueryHandler: return QuoteListItem
///               projections (Id, Author, Text, CreatedAt) without exposing IsDeleted or OwnerId.
///
/// What got simpler: the read handlers never load the full Quote entity, so there is
/// nothing to strip or hide — the DB projection IS the response shape.
/// </summary>
public class CqrsHandlerTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static AppDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={Path.Combine(Path.GetTempPath(), $"cqrs-test-{Guid.NewGuid():N}.db")}")
            .Options);

    private static TestClock Clock(DateTimeOffset at) => new(at);

    private static readonly DateTimeOffset _now =
        new(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);

    // ── CreateQuoteCommandHandler ────────────────────────────────────────────

    [Fact]
    public async Task CreateQuote_ValidCommand_ReturnsSuccessWithNewId()
    {
        using var ctx = BuildContext();
        ctx.Database.EnsureCreated();
        var handler = new CreateQuoteCommandHandler(new QuoteRepository(ctx), Clock(_now));

        var result = await handler.HandleAsync(
            new CreateQuoteCommand("Marcus Aurelius", "The obstacle is the way.", "user-1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeGreaterThan(0, "SQLite auto-increments from 1");
    }

    [Fact]
    public async Task CreateQuote_BlankAuthor_ReturnsFailWithMessage()
    {
        using var ctx = BuildContext();
        ctx.Database.EnsureCreated();
        var handler = new CreateQuoteCommandHandler(new QuoteRepository(ctx), Clock(_now));

        var result = await handler.HandleAsync(
            new CreateQuoteCommand("", "Some text.", null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Message.Should().Contain("Author");
    }

    [Fact]
    public async Task CreateQuote_AuthorTooLong_ReturnsFailWithMessage()
    {
        using var ctx = BuildContext();
        ctx.Database.EnsureCreated();
        var handler = new CreateQuoteCommandHandler(new QuoteRepository(ctx), Clock(_now));

        var result = await handler.HandleAsync(
            new CreateQuoteCommand(new string('A', 201), "Some text.", null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Message.Should().Contain("Author");
    }

    [Fact]
    public async Task CreateQuote_BlankText_ReturnsFailWithMessage()
    {
        using var ctx = BuildContext();
        ctx.Database.EnsureCreated();
        var handler = new CreateQuoteCommandHandler(new QuoteRepository(ctx), Clock(_now));

        var result = await handler.HandleAsync(
            new CreateQuoteCommand("Author", "   ", null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Message.Should().Contain("Text");
    }

    [Fact]
    public async Task CreateQuote_TextTooLong_ReturnsFailWithMessage()
    {
        using var ctx = BuildContext();
        ctx.Database.EnsureCreated();
        var handler = new CreateQuoteCommandHandler(new QuoteRepository(ctx), Clock(_now));

        var result = await handler.HandleAsync(
            new CreateQuoteCommand("Author", new string('x', 1001), null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Message.Should().Contain("Text");
    }

    [Fact]
    public async Task CreateQuote_ValidCommand_PersistsToDatabase()
    {
        using var ctx = BuildContext();
        ctx.Database.EnsureCreated();
        var handler = new CreateQuoteCommandHandler(new QuoteRepository(ctx), Clock(_now));

        var result = await handler.HandleAsync(
            new CreateQuoteCommand("Seneca", "Per aspera ad astra.", "user-42"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var stored = await ctx.Quotes.FindAsync(result.Value);
        stored.Should().NotBeNull();
        stored!.Author.Should().Be("Seneca");
        stored.Text.Should().Be("Per aspera ad astra.");
    }

    [Fact]
    public async Task CreateQuote_SetsCreatedAtFromClock()
    {
        using var ctx = BuildContext();
        ctx.Database.EnsureCreated();
        var handler = new CreateQuoteCommandHandler(new QuoteRepository(ctx), Clock(_now));

        var result = await handler.HandleAsync(
            new CreateQuoteCommand("Epictetus", "Not things but opinions.", null),
            CancellationToken.None);

        var stored = await ctx.Quotes.FindAsync(result.Value);
        stored!.CreatedAt.Should().Be(_now);
    }

    // ── GetQuotesQueryHandler ────────────────────────────────────────────────

    [Fact]
    public async Task GetQuotes_EmptyDatabase_ReturnsEmptyList()
    {
        using var ctx = BuildContext();
        ctx.Database.EnsureCreated();
        var handler = new GetQuotesQueryHandler(ctx);

        var items = await handler.HandleAsync(new GetQuotesQuery(1, 10), CancellationToken.None);

        items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetQuotes_ReturnsOnlyNonDeletedQuotes()
    {
        using var ctx = BuildContext();
        ctx.Database.EnsureCreated();
        var cmdHandler = new CreateQuoteCommandHandler(new QuoteRepository(ctx), Clock(_now));
        await cmdHandler.HandleAsync(new CreateQuoteCommand("Author A", "Visible quote.", null), CancellationToken.None);
        var deletedId = (await cmdHandler.HandleAsync(
            new CreateQuoteCommand("Author B", "Deleted quote.", null), CancellationToken.None)).Value;

        // Soft-delete the second quote directly.
        var q = await ctx.Quotes.FindAsync(deletedId);
        q!.SoftDelete();
        await ctx.SaveChangesAsync();

        var queryHandler = new GetQuotesQueryHandler(ctx);
        var items = await queryHandler.HandleAsync(new GetQuotesQuery(1, 10), CancellationToken.None);

        items.Should().HaveCount(1);
        items[0].Author.Should().Be("Author A");
    }

    [Fact]
    public async Task GetQuotes_ResultsAreQuoteListItems_NotFullEntities()
    {
        using var ctx = BuildContext();
        ctx.Database.EnsureCreated();
        var cmdHandler = new CreateQuoteCommandHandler(new QuoteRepository(ctx), Clock(_now));
        await cmdHandler.HandleAsync(new CreateQuoteCommand("Plato", "Know thyself.", "owner-1"), CancellationToken.None);

        var queryHandler = new GetQuotesQueryHandler(ctx);
        var items = await queryHandler.HandleAsync(new GetQuotesQuery(1, 10), CancellationToken.None);

        items.Should().ContainSingle();
        items[0].Should().BeOfType<QuoteListItem>("read model must be QuoteListItem, not Quote entity");
        // Confirm only screen-facing fields are exposed (no IsDeleted, no OwnerId).
        typeof(QuoteListItem).GetProperty("IsDeleted").Should().BeNull();
        typeof(QuoteListItem).GetProperty("OwnerId").Should().BeNull();
    }

    [Fact]
    public async Task GetQuotes_PaginationWorks()
    {
        using var ctx = BuildContext();
        ctx.Database.EnsureCreated();
        var cmdHandler = new CreateQuoteCommandHandler(new QuoteRepository(ctx), Clock(_now));
        for (var i = 1; i <= 5; i++)
            await cmdHandler.HandleAsync(new CreateQuoteCommand($"Author {i}", $"Quote {i}.", null), CancellationToken.None);

        var handler = new GetQuotesQueryHandler(ctx);
        var page1 = await handler.HandleAsync(new GetQuotesQuery(Page: 1, Size: 2), CancellationToken.None);
        var page2 = await handler.HandleAsync(new GetQuotesQuery(Page: 2, Size: 2), CancellationToken.None);
        var page3 = await handler.HandleAsync(new GetQuotesQuery(Page: 3, Size: 2), CancellationToken.None);

        page1.Should().HaveCount(2);
        page2.Should().HaveCount(2);
        page3.Should().HaveCount(1, "5 total, last page has 1 item");
        page1.Select(i => i.Id).Should().NotIntersectWith(page2.Select(i => i.Id));
    }

    // ── GetQuoteByIdQueryHandler ─────────────────────────────────────────────

    [Fact]
    public async Task GetQuoteById_ExistingId_ReturnsQuoteListItem()
    {
        using var ctx = BuildContext();
        ctx.Database.EnsureCreated();
        var cmdHandler = new CreateQuoteCommandHandler(new QuoteRepository(ctx), Clock(_now));
        var id = (await cmdHandler.HandleAsync(
            new CreateQuoteCommand("Aristotle", "Excellence is a habit.", null), CancellationToken.None)).Value;

        var handler = new GetQuoteByIdQueryHandler(ctx);
        var item = await handler.HandleAsync(new GetQuoteByIdQuery(id), CancellationToken.None);

        item.Should().NotBeNull();
        item!.Id.Should().Be(id);
        item.Author.Should().Be("Aristotle");
        item.Text.Should().Be("Excellence is a habit.");
        item.CreatedAt.Should().Be(_now);
    }

    [Fact]
    public async Task GetQuoteById_NonExistentId_ReturnsNull()
    {
        using var ctx = BuildContext();
        ctx.Database.EnsureCreated();
        var handler = new GetQuoteByIdQueryHandler(ctx);

        var item = await handler.HandleAsync(new GetQuoteByIdQuery(99999), CancellationToken.None);

        item.Should().BeNull();
    }

    [Fact]
    public async Task GetQuoteById_DeletedQuote_ReturnsNull()
    {
        using var ctx = BuildContext();
        ctx.Database.EnsureCreated();
        var cmdHandler = new CreateQuoteCommandHandler(new QuoteRepository(ctx), Clock(_now));
        var id = (await cmdHandler.HandleAsync(
            new CreateQuoteCommand("Deleted", "This quote is soft-deleted.", null), CancellationToken.None)).Value;

        var q = await ctx.Quotes.FindAsync(id);
        q!.SoftDelete();
        await ctx.SaveChangesAsync();

        var handler = new GetQuoteByIdQueryHandler(ctx);
        var item = await handler.HandleAsync(new GetQuoteByIdQuery(id), CancellationToken.None);

        item.Should().BeNull("soft-deleted quotes must be invisible to the read path");
    }
}

internal sealed class TestClock : IClock
{
    private readonly DateTimeOffset _now;
    public TestClock(DateTimeOffset now) => _now = now;
    public DateTimeOffset UtcNow => _now;
}
