namespace QuotesApi.Queries;

public record GetQuotesQuery(int Page, int Size, string? Author = null, string? Text = null);
