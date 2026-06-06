using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Data;
using QuotesApi.Dtos;
using Xunit;

namespace QuotesApi.Tests;

/// <summary>
/// Covers quote-endpoint branches not exercised by the authorization policy tests:
/// domain validation failures (→ 400), GET not-found (→ 404), and login failures (→ 401).
/// These paths exercise Result&lt;T&gt;.Fail, DomainError, and the early-return branches in
/// AuthEndpoints.Login that are absent from the other test suites.
/// </summary>
public class QuoteEndpointTests : IAsyncLifetime
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

    private async Task<LoginResponse> LoginAsync()
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = "test@example.com", password = "password123" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await resp.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    private HttpClient ClientWithToken(string token)
    {
        var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // ── GET /api/quotes/{id} ─────────────────────────────────────────────────

    /// <summary>An ID that was never created returns 404, not 500 or 200.</summary>
    [Fact]
    public async Task GetById_NonExistentId_Returns404()
    {
        var resp = await _client.GetAsync("/api/quotes/99999");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── POST /api/quotes — domain validation ─────────────────────────────────

    /// <summary>
    /// Blank text triggers Quote.Create → Result&lt;T&gt;.Fail → DomainError.
    /// The endpoint must return 400 with a ProblemDetails errors object.
    /// </summary>
    [Fact]
    public async Task CreateQuote_BlankText_Returns400WithValidationError()
    {
        var tokens = await LoginAsync();
        using var client = ClientWithToken(tokens.AccessToken);

        var resp = await client.PostAsJsonAsync("/api/quotes",
            new { author = "Valid Author", text = "" });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        doc.RootElement.TryGetProperty("errors", out _)
            .Should().BeTrue("ValidationProblem response must include an errors object");
    }

    /// <summary>Author longer than 200 characters is also rejected with 400.</summary>
    [Fact]
    public async Task CreateQuote_AuthorTooLong_Returns400WithValidationError()
    {
        var tokens = await LoginAsync();
        using var client = ClientWithToken(tokens.AccessToken);

        var resp = await client.PostAsJsonAsync("/api/quotes",
            new { author = new string('A', 201), text = "Valid text." });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── POST /api/auth/login — failure paths ─────────────────────────────────

    /// <summary>Wrong password for a valid email returns 401, not 500.</summary>
    [Fact]
    public async Task Login_WrongPassword_Returns401()
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = "test@example.com", password = "wrong-password" });

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>An email that has no account returns 401.</summary>
    [Fact]
    public async Task Login_UnknownEmail_Returns401()
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = "nobody@example.com", password = "password123" });

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── POST /api/auth/refresh — expiry path ─────────────────────────────────

    /// <summary>
    /// Directly ages the stored token in SQLite so IsExpired(now) returns true.
    /// Verifies the expiry guard in the refresh handler rejects the token with 401.
    /// Without this test, removing the expiry check would go undetected because no
    /// other test exercises a token that is past its ExpiresAt.
    /// </summary>
    [Fact]
    public async Task Refresh_WithExpiredToken_Returns401()
    {
        var tokens = await LoginAsync();

        // Age the stored token so it looks expired — bypass the 7-day window.
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = await db.RefreshTokens.OrderByDescending(t => t.Id).FirstAsync();
            stored.ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1);
            await db.SaveChangesAsync();
        }

        var resp = await _client.PostAsJsonAsync("/api/auth/refresh",
            new { refreshToken = tokens.RefreshToken });

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "a token past its ExpiresAt must be rejected even if not explicitly revoked");
    }
}
