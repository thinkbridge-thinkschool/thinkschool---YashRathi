namespace QuotesApi.Services;

public interface IExternalQuoteService
{
    Task<string[]> GetTagsAsync(int quoteId, CancellationToken ct = default);
}
