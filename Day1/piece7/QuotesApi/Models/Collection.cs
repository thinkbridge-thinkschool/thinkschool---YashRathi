namespace QuotesApi.Models;

public class CollectionItem
{
    public int QuoteId { get; private set; }
    public DateTime AddedAt { get; private set; }

    private CollectionItem() { }

    public CollectionItem(int quoteId)
    {
        QuoteId = quoteId;
        AddedAt = DateTime.UtcNow;
    }
}

public class Collection
{
    private List<CollectionItem> _items = new();

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string OwnerId { get; private set; } = string.Empty;
    public IReadOnlyList<CollectionItem> Items => _items.AsReadOnly();

    private Collection() { }

    public static Collection Create(string name, string ownerId)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length < 3 || trimmed.Length > 80)
            throw new ArgumentException("Collection name must be between 3 and 80 characters.");

        return new Collection
        {
            Id = Guid.NewGuid(),
            Name = trimmed,
            OwnerId = ownerId
        };
    }

    public void AddItem(int quoteId)
    {
        if (_items.Count >= 50)
            throw new InvalidOperationException("A collection cannot contain more than 50 items.");
        if (_items.Any(i => i.QuoteId == quoteId))
            throw new InvalidOperationException($"Quote {quoteId} is already in this collection.");

        _items.Add(new CollectionItem(quoteId));
    }

    public void RemoveItem(int quoteId)
    {
        var item = _items.FirstOrDefault(i => i.QuoteId == quoteId);
        if (item is null)
            throw new InvalidOperationException($"Quote {quoteId} is not in this collection.");

        _items.Remove(item);
    }
}
