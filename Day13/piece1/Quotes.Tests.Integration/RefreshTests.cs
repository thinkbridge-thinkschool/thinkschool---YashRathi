using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using QuotesApi.Dtos;
using Xunit;

namespace Quotes.Tests.Integration;

/// <summary>
/// Exercises the refresh-token rotation and logout flows against a real SQL Server DB.
/// These tests cover the RevokeFamilyAsync reuse-detection path that is absent from
/// the unit-test and policy-test suites.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class RefreshTests : IAsyncLifetime
{
    private readonly IntegrationFixture _fixture;
    private HttpClient _client = null!;

    public RefreshTests(SqlServerFixture serverFixture)
    {
        _fixture = new IntegrationFixture(serverFixture);
    }

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

    private Task<HttpResponseMessage> RefreshAsync(string token) =>
        _client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = token });

    private Task<HttpResponseMessage> LogoutAsync(string token) =>
        _client.PostAsJsonAsync("/api/auth/logout", new { refreshToken = token });

    /// <summary>Normal rotation: valid token returns new token pair; old token is consumed.</summary>
    [Fact]
    public async Task Refresh_ValidToken_Returns200WithNewTokenPair()
    {
        var first = await LoginAsync();

        var resp = await RefreshAsync(first.RefreshToken);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var second = await resp.Content.ReadFromJsonAsync<LoginResponse>();
        second!.AccessToken.Should().NotBeNullOrEmpty();
        second.RefreshToken.Should().NotBe(first.RefreshToken,
            "rotation must issue a different raw token each time");

        // Old token is single-use — must be rejected after rotation
        (await RefreshAsync(first.RefreshToken)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized, "consumed token must be invalid");
    }

    /// <summary>
    /// Reuse detection: presenting an already-replaced token triggers family revocation.
    /// RT1 is rotated into RT2; presenting RT1 again must revoke RT2 as well.
    /// </summary>
    [Fact]
    public async Task Refresh_ReuseDetected_RevokesEntireFamily()
    {
        var first = await LoginAsync();
        var rt1 = first.RefreshToken;

        var rotateResp = await RefreshAsync(rt1);
        var rt2 = (await rotateResp.Content.ReadFromJsonAsync<LoginResponse>())!.RefreshToken;

        // RT1 is already replaced — presenting it signals a potential credential leak
        (await RefreshAsync(rt1)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized, "replaced token triggers reuse detection");

        // RT2 must also be dead: the whole family was nuked
        (await RefreshAsync(rt2)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized, "family revocation must include descendant tokens");
    }

    /// <summary>A token hash that was never issued returns 401 immediately.</summary>
    [Fact]
    public async Task Refresh_UnknownToken_Returns401()
    {
        var resp = await RefreshAsync("bogus-token-that-was-never-issued");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>Logout revokes the token; a subsequent refresh must fail.</summary>
    [Fact]
    public async Task Logout_RevokesToken_SubsequentRefreshFails()
    {
        var tokens = await LoginAsync();

        (await LogoutAsync(tokens.RefreshToken)).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        (await RefreshAsync(tokens.RefreshToken)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized, "logged-out token must be revoked");
    }

    /// <summary>Logging out an already-revoked token is idempotent — 204, not an error.</summary>
    [Fact]
    public async Task Logout_AlreadyRevoked_StillReturns204()
    {
        var tokens = await LoginAsync();

        await LogoutAsync(tokens.RefreshToken);

        (await LogoutAsync(tokens.RefreshToken)).StatusCode
            .Should().Be(HttpStatusCode.NoContent, "logout must be idempotent");
    }
}
