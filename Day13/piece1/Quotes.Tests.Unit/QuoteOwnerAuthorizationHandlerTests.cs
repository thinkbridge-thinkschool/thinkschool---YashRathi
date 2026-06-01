using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using QuotesApi.Authorization;
using QuotesApi.Models;

namespace Quotes.Tests.Unit;

public class QuoteOwnerAuthorizationHandlerTests
{
    private readonly QuoteOwnerAuthorizationHandler _handler = new();
    private static readonly DateTimeOffset _now = new(2025, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleRequirementAsync_WhenSubMatchesOwnerId_Succeeds()
    {
        // Arrange
        var quote = Quote.Create("Author", "Text", _now, ownerId: "user-99").Value!;
        var principal = BuildPrincipal(sub: "user-99");
        var context = BuildContext(principal, quote);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleRequirementAsync_WhenSubDiffersFromOwnerId_DoesNotSucceed()
    {
        // Arrange
        var quote = Quote.Create("Author", "Text", _now, ownerId: "user-99").Value!;
        var principal = BuildPrincipal(sub: "user-77");
        var context = BuildContext(principal, quote);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleRequirementAsync_WhenSubClaimMissing_DoesNotSucceed()
    {
        // Arrange
        var quote = Quote.Create("Author", "Text", _now, ownerId: "user-99").Value!;
        var principal = new ClaimsPrincipal(new ClaimsIdentity(Array.Empty<Claim>()));
        var context = BuildContext(principal, quote);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleRequirementAsync_WhenOwnerIdIsNull_DoesNotSucceed()
    {
        // Arrange — quote with no owner (created anonymously)
        var quote = Quote.Create("Author", "Text", _now, ownerId: null).Value!;
        var principal = BuildPrincipal(sub: "user-99");
        var context = BuildContext(principal, quote);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeFalse();
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static ClaimsPrincipal BuildPrincipal(string sub) =>
        new(new ClaimsIdentity(new[] { new Claim("sub", sub) }, authenticationType: "test"));

    private static AuthorizationHandlerContext BuildContext(ClaimsPrincipal principal, Quote resource)
    {
        var requirement = new OwnerRequirement();
        return new AuthorizationHandlerContext(new[] { requirement }, principal, resource);
    }
}
