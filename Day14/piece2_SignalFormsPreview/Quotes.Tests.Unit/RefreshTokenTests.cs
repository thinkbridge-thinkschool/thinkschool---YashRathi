using FluentAssertions;
using QuotesApi.Models;

namespace Quotes.Tests.Unit;

public class RefreshTokenTests
{
    private static readonly DateTimeOffset _baseTime = new(2025, 6, 1, 12, 0, 0, TimeSpan.Zero);

    // ── IsRevoked ────────────────────────────────────────────────────────────

    [Fact]
    public void IsRevoked_WhenRevokedAtIsNull_ReturnsFalse()
    {
        // Arrange
        var token = new RefreshToken { RevokedAt = null };

        // Assert
        token.IsRevoked.Should().BeFalse();
    }

    [Fact]
    public void IsRevoked_WhenRevokedAtIsSet_ReturnsTrue()
    {
        // Arrange
        var token = new RefreshToken { RevokedAt = _baseTime };

        // Assert
        token.IsRevoked.Should().BeTrue();
    }

    // ── IsExpired ────────────────────────────────────────────────────────────

    [Fact]
    public void IsExpired_WhenNowIsBeforeExpiry_ReturnsFalse()
    {
        // Arrange
        var token = new RefreshToken { ExpiresAt = _baseTime.AddDays(7) };

        // Act
        var expired = token.IsExpired(_baseTime);

        // Assert
        expired.Should().BeFalse();
    }

    [Fact]
    public void IsExpired_WhenNowIsAfterExpiry_ReturnsTrue()
    {
        // Arrange
        var token = new RefreshToken { ExpiresAt = _baseTime.AddDays(-1) };

        // Act
        var expired = token.IsExpired(_baseTime);

        // Assert
        expired.Should().BeTrue();
    }

    [Fact]
    public void IsExpired_WhenNowExactlyEqualsExpiry_ReturnsFalse()
    {
        // Arrange — IsExpired uses strictly >, so exactly at ExpiresAt is NOT expired
        var token = new RefreshToken { ExpiresAt = _baseTime };

        // Act
        var expired = token.IsExpired(_baseTime);

        // Assert
        expired.Should().BeFalse();
    }

    [Fact]
    public void IsExpired_WhenNowIsOneTickPastExpiry_ReturnsTrue()
    {
        // Arrange
        var token = new RefreshToken { ExpiresAt = _baseTime };

        // Act
        var expired = token.IsExpired(_baseTime.AddTicks(1));

        // Assert
        expired.Should().BeTrue();
    }

    // ── IsActive ─────────────────────────────────────────────────────────────

    [Fact]
    public void IsActive_WhenNotRevokedAndNotExpired_ReturnsTrue()
    {
        // Arrange
        var token = new RefreshToken
        {
            ExpiresAt = _baseTime.AddDays(7),
            RevokedAt = null
        };

        // Act
        var active = token.IsActive(_baseTime);

        // Assert
        active.Should().BeTrue();
    }

    [Fact]
    public void IsActive_WhenRevoked_ReturnsFalse()
    {
        // Arrange
        var token = new RefreshToken
        {
            ExpiresAt = _baseTime.AddDays(7),
            RevokedAt = _baseTime.AddMinutes(-5)
        };

        // Act
        var active = token.IsActive(_baseTime);

        // Assert
        active.Should().BeFalse();
    }

    [Fact]
    public void IsActive_WhenExpired_ReturnsFalse()
    {
        // Arrange
        var token = new RefreshToken
        {
            ExpiresAt = _baseTime.AddDays(-1),
            RevokedAt = null
        };

        // Act
        var active = token.IsActive(_baseTime);

        // Assert
        active.Should().BeFalse();
    }

    [Fact]
    public void IsActive_WhenBothRevokedAndExpired_ReturnsFalse()
    {
        // Arrange
        var token = new RefreshToken
        {
            ExpiresAt = _baseTime.AddDays(-1),
            RevokedAt = _baseTime.AddDays(-2)
        };

        // Act
        var active = token.IsActive(_baseTime);

        // Assert
        active.Should().BeFalse();
    }
}
