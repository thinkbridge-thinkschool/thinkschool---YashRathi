using FluentAssertions;
using QuotesApi.Models;

namespace Quotes.Tests.Unit;

public class QuoteCreateTests
{
    private static readonly DateTimeOffset _now = new(2025, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_ValidAuthorAndText_ReturnsSuccess()
    {
        // Arrange
        var author = "Mark Twain";
        var text = "The secret of getting ahead is getting started.";

        // Act
        var result = Quote.Create(author, text, _now);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Author.Should().Be(author);
        result.Value.Text.Should().Be(text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankAuthor_ReturnsFailureWithMessage(string? author)
    {
        // Act
        var result = Quote.Create(author!, "Some text", _now);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Message.Should().Contain("Author");
    }

    [Fact]
    public void Create_AuthorExceedsMaxLength_ReturnsFailure()
    {
        // Arrange
        var author = new string('A', 201);

        // Act
        var result = Quote.Create(author, "Some text", _now);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Message.Should().Contain("Author");
    }

    [Fact]
    public void Create_AuthorAtMaxLength_ReturnsSuccess()
    {
        // Arrange
        var author = new string('A', 200);

        // Act
        var result = Quote.Create(author, "Some text", _now);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankText_ReturnsFailureWithMessage(string? text)
    {
        // Act
        var result = Quote.Create("Author", text!, _now);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Message.Should().Contain("Text");
    }

    [Fact]
    public void Create_TextExceedsMaxLength_ReturnsFailure()
    {
        // Arrange
        var text = new string('T', 1001);

        // Act
        var result = Quote.Create("Author", text, _now);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Message.Should().Contain("Text");
    }

    [Fact]
    public void Create_TextAtMaxLength_ReturnsSuccess()
    {
        // Arrange
        var text = new string('T', 1000);

        // Act
        var result = Quote.Create("Author", text, _now);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Create_ValidInputs_SetsCreatedAt()
    {
        // Act
        var result = Quote.Create("Author", "Text", _now);

        // Assert
        result.Value!.CreatedAt.Should().Be(_now);
    }

    [Fact]
    public void Create_AuthorWithSurroundingWhitespace_TrimsIt()
    {
        // Act
        var result = Quote.Create("  Mark Twain  ", "  Some quote  ", _now);

        // Assert
        result.Value!.Author.Should().Be("Mark Twain");
        result.Value.Text.Should().Be("Some quote");
    }

    [Fact]
    public void Create_WithOwnerId_SetsOwnerId()
    {
        // Arrange
        var ownerId = "user-42";

        // Act
        var result = Quote.Create("Author", "Text", _now, ownerId);

        // Assert
        result.Value!.OwnerId.Should().Be(ownerId);
    }

    [Fact]
    public void Create_WithoutOwnerId_OwnerIdIsNull()
    {
        // Act
        var result = Quote.Create("Author", "Text", _now);

        // Assert
        result.Value!.OwnerId.Should().BeNull();
    }

    [Fact]
    public void Create_NewQuote_IsDeletedIsFalse()
    {
        // Act
        var result = Quote.Create("Author", "Text", _now);

        // Assert
        result.Value!.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void SoftDelete_OnLiveQuote_SetsIsDeletedTrue()
    {
        // Arrange
        var quote = Quote.Create("Author", "Text", _now).Value!;

        // Act
        quote.SoftDelete();

        // Assert
        quote.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public void SoftDelete_CalledTwice_StaysDeleted()
    {
        // Arrange
        var quote = Quote.Create("Author", "Text", _now).Value!;

        // Act
        quote.SoftDelete();
        quote.SoftDelete();

        // Assert
        quote.IsDeleted.Should().BeTrue();
    }
}
