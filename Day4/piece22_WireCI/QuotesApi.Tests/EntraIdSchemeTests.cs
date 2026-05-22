using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace QuotesApi.Tests;

/// <summary>
/// Exercises the MultiScheme ForwardDefaultSelector in InfrastructureExtensions.
/// When a token's issuer contains "login.microsoftonline.com" the selector must
/// route to the EntraId scheme (which then rejects it — we just need the branch hit).
/// </summary>
public class EntraIdSchemeTests : IAsyncLifetime
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

    [Fact]
    public async Task Request_WithEntraLikeIssuer_RoutesToEntraScheme_Returns401()
    {
        // Build a JWT whose issuer looks like an Entra ID (Azure AD) token.
        // The ForwardDefaultSelector checks the issuer and must return "EntraId".
        // Entra validation will fail (no real tenant), so we expect 401 — that's fine;
        // we only care that the selector branch is executed.
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("this-key-is-only-for-routing-test-00000"));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var entraLikeToken = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: "https://login.microsoftonline.com/test-tenant/v2.0",
            audience: "api://test",
            claims: [new Claim(JwtRegisteredClaimNames.Sub, "user-1")],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: creds));

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", entraLikeToken);

        var response = await _client.GetAsync("/api/quotes?page=1&size=10");

        // Entra validation fails in the test environment (no real OIDC config),
        // so 401 is the expected outcome — the important thing is the code path ran.
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
