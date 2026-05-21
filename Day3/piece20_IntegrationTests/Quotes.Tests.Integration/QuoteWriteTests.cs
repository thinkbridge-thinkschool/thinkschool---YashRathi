using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using QuotesApi.Dtos;
using Xunit;

namespace Quotes.Tests.Integration;

/// <summary>
/// POST /api/quotes — requires authentication (JWT Bearer) and the can-edit-quotes policy
/// (scope = quotes.write claim). Covers the success path, both auth failure modes,
/// and the domain-validation error path.
/// </summary>
public sealed class QuoteWriteTests : IAsyncLifetime
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

    private async Task<string> GetAccessTokenAsync()
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = "test@example.com", password = "password123" });
        return (await resp.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
    }

    /// <summary>Valid token with scope → 201 Created and a Location header pointing to the new resource.</summary>
    [Fact]
    public async Task CreateQuote_ValidToken_Returns201WithLocationHeader()
    {
        var token = await GetAccessTokenAsync();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var resp = await _client.PostAsJsonAsync("/api/quotes",
            new { author = "Marcus Aurelius", text = "The obstacle is the way." });

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        resp.Headers.Location.Should().NotBeNull();
        resp.Headers.Location!.ToString().Should().Contain("/api/quotes/");
    }

    /// <summary>No Authorization header → authentication fails → 401.</summary>
    [Fact]
    public async Task CreateQuote_NoToken_Returns401()
    {
        var resp = await _client.PostAsJsonAsync("/api/quotes",
            new { author = "Epictetus", text = "It is not things that disturb us." });

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Token is valid and correctly signed but lacks scope = quotes.write.
    /// Authentication passes, authorization (can-edit-quotes policy) fails → 403.
    /// </summary>
    [Fact]
    public async Task CreateQuote_TokenWithoutScopeClaim_Returns403()
    {
        var tokenWithoutScope = _fixture.BuildToken([
            new Claim(JwtRegisteredClaimNames.Sub, "1"),
            new Claim(JwtRegisteredClaimNames.Email, "test@example.com"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            // deliberately omitting: new Claim("scope", "quotes.write")
        ]);

        using var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokenWithoutScope);

        var resp = await client.PostAsJsonAsync("/api/quotes",
            new { author = "Epictetus", text = "Some quote text." });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Auth passes but domain validation rejects an empty Author.
    /// Quote.Create returns Fail → endpoint calls Results.ValidationProblem → 400
    /// with ProblemDetails body containing an "errors" property.
    /// </summary>
    [Fact]
    public async Task CreateQuote_BlankAuthor_Returns400WithProblemDetails()
    {
        var token = await GetAccessTokenAsync();
        using var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var resp = await client.PostAsJsonAsync("/api/quotes",
            new { author = "", text = "Valid quote text here." });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        doc.RootElement.TryGetProperty("errors", out _)
            .Should().BeTrue("ValidationProblem response must include an errors object");
    }
}
