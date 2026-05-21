using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using FluentAssertions;
using QuotesApi.Dtos;
using Xunit;

namespace Quotes.Tests.Integration;

/// <summary>
/// DELETE /api/quotes/{id} — requires authentication and passes through resource-based
/// authorization (can-delete-own-quote: JWT sub must match Quote.OwnerId).
/// </summary>
public sealed class QuoteDeleteTests : IAsyncLifetime
{
    private readonly IntegrationFixture _fixture = new();
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _client = _fixture.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _fixture.DisposeAsync();
    }

    private async Task<LoginResponse> LoginAsync()
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = "test@example.com", password = "password123" });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    private async Task<string> CreateQuoteAsync(string accessToken)
    {
        using var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);
        var resp = await client.PostAsJsonAsync("/api/quotes",
            new { author = "Test Author", text = "Test quote text." });
        resp.EnsureSuccessStatusCode();
        return resp.Headers.Location!.ToString().Split('/').Last();
    }

    /// <summary>The user who created the quote can delete it — 204 No Content.</summary>
    [Fact]
    public async Task DeleteQuote_ByOwner_Returns204()
    {
        var tokens = await LoginAsync();
        var id = await CreateQuoteAsync(tokens.AccessToken);

        using var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var resp = await client.DeleteAsync($"/api/quotes/{id}");

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// A different user (valid JWT, correct scope, but different sub) must be denied — 403.
    /// The resource-based QuoteOwnerAuthorizationHandler compares JWT sub to Quote.OwnerId.
    /// </summary>
    [Fact]
    public async Task DeleteQuote_ByNonOwner_Returns403()
    {
        var ownerTokens = await LoginAsync();
        var id = await CreateQuoteAsync(ownerTokens.AccessToken);

        // userId=999 does not exist in the DB but that's fine — JWT is still valid
        var strangerToken = _fixture.BuildToken([
            new Claim(JwtRegisteredClaimNames.Sub, "999"),
            new Claim(JwtRegisteredClaimNames.Email, "stranger@example.com"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim("scope", "quotes.write")
        ]);

        using var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", strangerToken);

        var resp = await client.DeleteAsync($"/api/quotes/{id}");

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>No Authorization header → 401 before even reaching ownership check.</summary>
    [Fact]
    public async Task DeleteQuote_NoToken_Returns401()
    {
        var resp = await _client.DeleteAsync("/api/quotes/1");

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>Authenticated but the quote doesn't exist → 404.</summary>
    [Fact]
    public async Task DeleteQuote_NonExistentId_Returns404()
    {
        var tokens = await LoginAsync();

        using var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var resp = await client.DeleteAsync("/api/quotes/99999");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Soft-delete means the record stays in the DB with IsDeleted=true.
    /// GetByIdAsync filters it out, so a second DELETE on the same ID returns 404.
    /// </summary>
    [Fact]
    public async Task DeleteQuote_AlreadyDeleted_Returns404OnSecondCall()
    {
        var tokens = await LoginAsync();
        var id = await CreateQuoteAsync(tokens.AccessToken);

        using var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        (await client.DeleteAsync($"/api/quotes/{id}")).StatusCode
            .Should().Be(HttpStatusCode.NoContent, "first delete must succeed");

        (await client.DeleteAsync($"/api/quotes/{id}")).StatusCode
            .Should().Be(HttpStatusCode.NotFound, "soft-deleted quote is invisible to subsequent requests");
    }
}
