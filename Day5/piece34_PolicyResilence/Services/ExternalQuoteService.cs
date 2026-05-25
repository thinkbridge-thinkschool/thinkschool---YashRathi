using System.Net.Http.Json;

namespace QuotesApi.Services;

public sealed class ExternalQuoteService(IHttpClientFactory factory) : IExternalQuoteService
{
    public const string ClientName = "external-quotes";

    public async Task<string[]> GetTagsAsync(int quoteId, CancellationToken ct = default)
    {
        var client = factory.CreateClient(ClientName);
        var response = await client.GetAsync($"/quotes/{quoteId}/tags", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<string[]>(ct) ?? [];
    }
}
