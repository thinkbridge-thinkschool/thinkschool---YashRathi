namespace QuotesApi.Dtos;

public class CreateQuoteRequest
{
    public string Author { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;
}