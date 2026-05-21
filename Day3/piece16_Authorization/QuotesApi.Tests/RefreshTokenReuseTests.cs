using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using QuotesApi.Dtos;
using Xunit;

namespace QuotesApi.Tests;

/// <summary>
/// Spins up the real ASP.NET Core host against an isolated temp SQLite file.
/// Each instance creates a unique DB so test classes are isolated.
/// </summary>
internal sealed class ApiFixture : WebApplicationFactory<Program>
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"refresh-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, cfg) =>
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = $"Data Source={_dbPath}"
            }));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { File.Delete(_dbPath); } catch { /* best-effort */ }
    }
}

/// <summary>
/// Integration tests for refresh-token rotation and reuse-detection.
///
/// The key security property under test:
///   If a refresh token that has already been replaced is presented again
///   (reuse), the server must revoke every token in that family and force
///   the user to log in again.
/// </summary>
public class RefreshTokenReuseTests : IAsyncLifetime
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

    private Task<HttpResponseMessage> RefreshAsync(string rawToken) =>
        _client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = rawToken });

    private Task<HttpResponseMessage> LogoutAsync(string rawToken) =>
        _client.PostAsJsonAsync("/api/auth/logout", new { refreshToken = rawToken });

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NormalRotation_ReturnsNewTokenPair_And_OldTokenIsInvalidated()
    {
        var first = await LoginAsync();

        // Use RT1 → should succeed and return a fresh pair
        var rotateResp = await RefreshAsync(first.RefreshToken);
        rotateResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var second = (await rotateResp.Content.ReadFromJsonAsync<LoginResponse>())!;

        second.RefreshToken.Should().NotBe(first.RefreshToken);
        second.AccessToken.Should().NotBeNullOrEmpty();

        // RT1 is now consumed (single-use) — must be rejected
        var staleResp = await RefreshAsync(first.RefreshToken);
        staleResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "RT1 was single-use and must be revoked after rotation");
    }

    /// <summary>
    /// THE core security test:
    ///
    /// Login  →  RT1
    ///   ↓ rotate
    ///          RT2   (RT1 revoked + replaced)
    ///   ↓ present RT1 again  ← REUSE DETECTED
    ///
    /// Expected: server revokes the entire family → RT2 also rejected.
    /// </summary>
    [Fact]
    public async Task ReuseDetection_RevokesEntireFamily_And_BlocksValidDescendant()
    {
        // Step 1 — login → RT1
        var first = await LoginAsync();
        var rt1 = first.RefreshToken;

        // Step 2 — rotate RT1 → RT2  (RT1 is now revoked / replaced)
        var rotateResp = await RefreshAsync(rt1);
        rotateResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var rt2 = (await rotateResp.Content.ReadFromJsonAsync<LoginResponse>())!.RefreshToken;

        // Step 3 — present the already-replaced RT1 again
        //   Server must: detect reuse, revoke the whole family, return 401
        var reuseResp = await RefreshAsync(rt1);
        reuseResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "presenting a replaced token signals a credential leak");

        // Step 4 — RT2 was valid before reuse was detected, but the family
        //   was nuked in step 3 — it must also be dead now
        var rt2Resp = await RefreshAsync(rt2);
        rt2Resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the entire token family must be revoked when reuse is detected");
    }

    [Fact]
    public async Task Logout_RevokesToken_PreventsSubsequentRefresh()
    {
        var tokens = await LoginAsync();

        var logoutResp = await LogoutAsync(tokens.RefreshToken);
        logoutResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var refreshResp = await RefreshAsync(tokens.RefreshToken);
        refreshResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "logged-out token should be revoked");
    }

    [Fact]
    public async Task Logout_IsIdempotent_SecondCallReturns204()
    {
        var tokens = await LoginAsync();

        await LogoutAsync(tokens.RefreshToken);

        // Second logout of the already-revoked token must still return 204
        var second = await LogoutAsync(tokens.RefreshToken);
        second.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Refresh_WithUnknownToken_Returns401()
    {
        var resp = await RefreshAsync("completely-bogus-token-never-issued");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task MultipleSessions_AreIndependent_ReuseInOneDoesNotKillOther()
    {
        // Two independent logins → two separate families
        var sessionA = await LoginAsync();
        var sessionB = await LoginAsync();

        // Rotate session A: RT_A1 → RT_A2
        var rotateA = await RefreshAsync(sessionA.RefreshToken);
        var tokenA2 = (await rotateA.Content.ReadFromJsonAsync<LoginResponse>())!.RefreshToken;

        // Trigger reuse in session A (re-present RT_A1)
        (await RefreshAsync(sessionA.RefreshToken)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

        // RT_A2 should be dead — family A was nuked
        (await RefreshAsync(tokenA2)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized, "family A is revoked");

        // Session B must still work — it is a different family
        (await RefreshAsync(sessionB.RefreshToken)).StatusCode
            .Should().Be(HttpStatusCode.OK, "family B is unrelated and must remain active");
    }
}
