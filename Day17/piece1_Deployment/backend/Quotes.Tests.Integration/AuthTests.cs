using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using QuotesApi.Dtos;
using Xunit;

namespace Quotes.Tests.Integration;

/// <summary>
/// Happy + error paths for POST /api/auth/login.
/// Also implicitly verifies that EF migrations ran (seeded user is queryable).
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class AuthTests : IAsyncLifetime
{
    private readonly IntegrationFixture _fixture;
    private HttpClient _client = null!;

    public AuthTests(SqlServerFixture serverFixture)
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

    [Fact]
    public async Task Login_ValidCredentials_Returns200WithTokenPair()
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = "test@example.com", password = "password123" });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<LoginResponse>();
        body!.AccessToken.Should().NotBeNullOrEmpty();
        body.RefreshToken.Should().NotBeNullOrEmpty();
        body.ExpiresIn.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401()
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = "test@example.com", password = "wrong-password" });

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_UnknownEmail_Returns401()
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = "nobody@example.com", password = "password123" });

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
