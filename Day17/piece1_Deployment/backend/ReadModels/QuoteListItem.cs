namespace QuotesApi.ReadModels;

public record QuoteListItem(int Id, string Author, string Text, DateTimeOffset CreatedAt);
