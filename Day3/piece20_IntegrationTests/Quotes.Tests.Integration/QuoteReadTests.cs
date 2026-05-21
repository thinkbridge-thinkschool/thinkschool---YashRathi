using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using QuotesApi.Dtos;
using Xunit;

namespace Quotes.Tests.Integration;

/// <summary>
/// Read-only endpoints are public — no auth required.
/// Each test creates whatever data it needs, so tests are fully self-contained.
/// </summary>
public sealed class QuoteReadTests : IAsyncLifetime
{
    private readonly IntegrationFixture _fixture = new();
    private HttpClient _client = null!;

    // Local DTO to deserialize Quote responses without needing private-setter access.
    private record QuoteDto(int Id, string Author, string Text, DateTimeOffset CreatedAt, bool IsDeleted, string? OwnerId);

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

    /// <summary>Fresh DB has no quotes — verifies the list endpoint works and returns an array.</summary>
    [Fact]
    public async Task GetAll_EmptyDatabase_Returns200WithEmptyArray()
    {
        var resp = await _client.GetAsync("/api/quotes?page=1&size=10");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        doc.RootElement.GetArrayLength().Should().Be(0);
    }

    /// <summary>Creates a quote via the authenticated POST, then retrieves it by ID.</summary>
    [Fact]
    public async Task GetById_ExistingQuote_Returns200WithCorrectData()
    {
        var token = await GetAccessTokenAsync();
        using var authClient = _fixture.CreateClient();
        authClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var createResp = await authClient.PostAsJsonAsync("/api/quotes",
            new { author = "Seneca", text = "Per aspera ad astra." });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = createResp.Headers.Location!.ToString().Split('/').Last();

        var resp = await _client.GetAsync($"/api/quotes/{id}");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var quote = await resp.Content.ReadFromJsonAsync<QuoteDto>();
        quote!.Author.Should().Be("Seneca");
        quote.Text.Should().Be("Per aspera ad astra.");
        quote.IsDeleted.Should().BeFalse();
    }

    /// <summary>A quote ID that was never created must return 404, not 500.</summary>
    [Fact]
    public async Task GetById_NonExistentId_Returns404()
    {
        var resp = await _client.GetAsync("/api/quotes/99999");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
