namespace QuotesApi.Cache;

internal static class CacheKeys
{
    public static string QuoteById(int id) => $"q:id:{id}";

    public static string QuotesList(int page, int size, string? author, string? text)
        => $"q:list:{page}:{size}:{author ?? ""}:{text ?? ""}";

    public const string ByAuthor = "q:by-author";

    // Tag shared by all list/collection entries — RemoveByTagAsync(TagLists) clears every
    // paginated page and the by-author summary in one call.
    public const string TagLists = "q:lists";

    // Tag shared by all single-quote entries — used to bulk-clear during dev/testing.
    public const string TagIds = "q:ids";
}
