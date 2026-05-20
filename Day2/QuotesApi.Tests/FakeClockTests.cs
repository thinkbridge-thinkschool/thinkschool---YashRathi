using QuotesApi.Abstractions;
using QuotesApi.Models;
namespace QuotesApi.Tests;

public class FakeClock : IClock
{
    private readonly DateTimeOffset _fixedTime;
    public FakeClock(DateTimeOffset fixedTime) => _fixedTime = fixedTime;
    public DateTimeOffset UtcNow => _fixedTime;
}

public class FakeClockTests
{
    [Fact]
    public void Quote_CreatedAt_UsesFakeClock()
    {
        // Arrange
        var frozenTime = new DateTimeOffset(2025, 1, 15, 0, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(frozenTime);

        // Act
        var quote = new Quote
        {
            Author = "Yash",
            Text = "DI is testable",
            CreatedAt = clock.UtcNow
        };

        // Assert
        Assert.Equal(frozenTime, quote.CreatedAt);
    }
}