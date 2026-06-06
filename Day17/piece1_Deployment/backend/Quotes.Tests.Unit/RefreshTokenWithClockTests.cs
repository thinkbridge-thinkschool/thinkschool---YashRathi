using FluentAssertions;
using NSubstitute;
using QuotesApi.Abstractions;
using QuotesApi.Models;

namespace Quotes.Tests.Unit;

/// Tests that show how IClock is injected into time-sensitive logic.
/// FakeClock lets tests control the passage of time deterministically;
/// NSubstitute is used to demonstrate the substitution pattern inline.
public class RefreshTokenWithClockTests
{
    // ── FakeClock ─────────────────────────────────────────────────────────────

    [Fact]
    public void IsExpired_WithFakeClock_ReturnsFalseBeforeExpiry()
    {
        // Arrange
        var clock = new FakeClock(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var token = new RefreshToken { ExpiresAt = clock.UtcNow.AddDays(7) };

        // Act
        var expired = token.IsExpired(clock.UtcNow);

        // Assert
        expired.Should().BeFalse();
    }

    [Fact]
    public void IsExpired_WithFakeClock_ReturnsTrueAfterAdvancingPastExpiry()
    {
        // Arrange
        var clock = new FakeClock(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var token = new RefreshToken { ExpiresAt = clock.UtcNow.AddDays(7) };

        clock.Advance(TimeSpan.FromDays(8));   // jump past the 7-day expiry

        // Act
        var expired = token.IsExpired(clock.UtcNow);

        // Assert
        expired.Should().BeTrue();
    }

    [Fact]
    public void IsActive_WithFakeClock_TransitionsFalseWhenClockPassesExpiry()
    {
        // Arrange
        var clock = new FakeClock(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var token = new RefreshToken
        {
            ExpiresAt = clock.UtcNow.AddDays(7),
            RevokedAt = null
        };

        token.IsActive(clock.UtcNow).Should().BeTrue("token is still fresh");

        clock.Advance(TimeSpan.FromDays(8));

        // Act
        var active = token.IsActive(clock.UtcNow);

        // Assert
        active.Should().BeFalse("token has now expired");
    }

    // ── NSubstitute ───────────────────────────────────────────────────────────

    [Fact]
    public void IsActive_WithNSubstituteClock_ReturnsTrueWhenFresh()
    {
        // Arrange
        var fixedNow = new DateTimeOffset(2025, 3, 15, 10, 0, 0, TimeSpan.Zero);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(fixedNow);

        var token = new RefreshToken
        {
            ExpiresAt = fixedNow.AddDays(7),
            RevokedAt = null
        };

        // Act
        var active = token.IsActive(clock.UtcNow);

        // Assert
        active.Should().BeTrue();
    }

    [Fact]
    public void IsActive_WithNSubstituteClock_ReturnsFalseAfterSevenDays()
    {
        // Arrange
        var mintedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(mintedAt.AddDays(10)); // simulates 10 days later

        var token = new RefreshToken
        {
            ExpiresAt = mintedAt.AddDays(7),
            RevokedAt = null
        };

        // Act
        var active = token.IsActive(clock.UtcNow);

        // Assert
        active.Should().BeFalse();
    }

    [Fact]
    public void IsActive_WithNSubstituteClock_RevocationBeatsExpiry()
    {
        // Arrange — token not yet expired but already revoked (reuse detection scenario)
        var fixedNow = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(fixedNow);

        var token = new RefreshToken
        {
            ExpiresAt = fixedNow.AddDays(5),              // still valid by time
            RevokedAt = fixedNow.AddMinutes(-1)            // but already revoked
        };

        // Act
        var active = token.IsActive(clock.UtcNow);

        // Assert
        active.Should().BeFalse();
    }
}
