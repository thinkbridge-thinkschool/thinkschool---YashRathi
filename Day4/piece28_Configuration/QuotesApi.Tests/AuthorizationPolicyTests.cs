using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using QuotesApi.Dtos;
using QuotesApi.Options;
using Xunit;

namespace QuotesApi.Tests;

/// <summary>
/// Tests that authorization policies gate endpoints correctly.
///
/// Policy 1 — "can-edit-quotes" (claim-based):
///   POST /api/quotes requires scope = quotes.write in the JWT.
///   Missing claim → 403.
///
/// Policy 2 — "can-delete-own-quote" (resource-based IAuthorizationRequirement):
///   DELETE /api/quotes/{id} checks that the JWT sub matches the quote's OwnerId.
///   Different user → 403.
/// </summary>
public class AuthorizationPolicyTests : IAsyncLifetime
{
    private readonly ApiFixture _fixture = new();
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

    // ── helpers ───────────────────────────────────────────────────────────────

    private async Task<LoginResponse> LoginAsync()
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = "test@example.com", password = "password123" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await resp.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    /// <summary>
    /// Builds a valid, signed JWT using the app's own key — but with caller-supplied claims.
    /// Use this to craft tokens that deliberately omit or alter specific claims.
    /// </summary>
    private string BuildCustomToken(IEnumerable<Claim> claims)
    {
        var opts = _fixture.Services.GetRequiredService<IOptions<JwtOptions>>().Value;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opts.Key));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var jwt = new JwtSecurityToken(
            issuer: opts.Issuer,
            audience: opts.Audience,
            claims: claims,
            expires: DateTime.UtcNow.Add(opts.AccessTokenLifetime),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private HttpClient ClientWithToken(string token)
    {
        var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // ── Policy 1: can-edit-quotes (claim-based) ───────────────────────────────

    [Fact]
    public async Task CreateQuote_WithoutScopeClaim_Returns403()
    {
        // Token has sub + email but NO scope claim → policy fails
        var tokenWithoutScope = BuildCustomToken([
            new Claim(JwtRegisteredClaimNames.Sub, "1"),
            new Claim(JwtRegisteredClaimNames.Email, "test@example.com"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        ]);

        using var client = ClientWithToken(tokenWithoutScope);
        var resp = await client.PostAsJsonAsync("/api/quotes",
            new { author = "Aurelius", text = "The obstacle is the way." });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "scope = quotes.write is required by the can-edit-quotes policy");
    }

    [Fact]
    public async Task CreateQuote_WithScopeClaim_Returns201()
    {
        // Normal login issues a token that includes scope = quotes.write
        var tokens = await LoginAsync();

        using var client = ClientWithToken(tokens.AccessToken);
        var resp = await client.PostAsJsonAsync("/api/quotes",
            new { author = "Aurelius", text = "The obstacle is the way." });

        resp.StatusCode.Should().Be(HttpStatusCode.Created,
            "a token with scope = quotes.write satisfies can-edit-quotes");
    }

    // ── Policy 2: can-delete-own-quote (resource-based) ──────────────────────

    [Fact]
    public async Task DeleteQuote_ByNonOwner_Returns403()
    {
        // Step 1 — owner (userId=1) creates a quote
        var ownerTokens = await LoginAsync();
        using var ownerClient = ClientWithToken(ownerTokens.AccessToken);

        var createResp = await ownerClient.PostAsJsonAsync("/api/quotes",
            new { author = "Seneca", text = "Per aspera ad astra." });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);

        var location = createResp.Headers.Location!.ToString();
        var quoteId = location.Split('/').Last();

        // Step 2 — a different user (userId=999, no real DB record needed — JWT is enough)
        var strangerToken = BuildCustomToken([
            new Claim(JwtRegisteredClaimNames.Sub, "999"),
            new Claim(JwtRegisteredClaimNames.Email, "stranger@example.com"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim("scope", "quotes.write")
        ]);

        using var strangerClient = ClientWithToken(strangerToken);
        var deleteResp = await strangerClient.DeleteAsync($"/api/quotes/{quoteId}");

        deleteResp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "a user who did not create the quote must be denied by can-delete-own-quote");
    }

    [Fact]
    public async Task DeleteQuote_ByOwner_Returns204()
    {
        // Owner creates and then deletes their own quote
        var tokens = await LoginAsync();
        using var client = ClientWithToken(tokens.AccessToken);

        var createResp = await client.PostAsJsonAsync("/api/quotes",
            new { author = "Seneca", text = "Dum spiro spero." });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);

        var location = createResp.Headers.Location!.ToString();
        var quoteId = location.Split('/').Last();

        var deleteResp = await client.DeleteAsync($"/api/quotes/{quoteId}");

        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "the quote owner satisfies can-delete-own-quote");
    }
}
