namespace ServiceBusDemo.Models;

public record QuoteMessage(
    string Id,
    string Author,
    string Text,
    bool IsPoisonous = false);
