namespace QuotesApi.Commands;

public record CreateQuoteCommand(string Author, string Text, string? OwnerId);
