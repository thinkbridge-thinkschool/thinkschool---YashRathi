# Read Models + CQRS-lite — Exercise Answer

## Command Handler (Write Path)

```csharp
// Commands/CreateQuoteCommand.cs
public record CreateQuoteCommand(string Author, string Text, string? OwnerId);
```

```csharp
// Commands/CreateQuoteCommandHandler.cs
public sealed class CreateQuoteCommandHandler
{
    private readonly IQuoteRepository _repository;
    private readonly IClock _clock;

    public CreateQuoteCommandHandler(IQuoteRepository repository, IClock clock)
    {
        _repository = repository;
        _clock = clock;
    }

    // Returns the new quote's ID, or a domain error if validation fails.
    public async Task<Result<int>> HandleAsync(CreateQuoteCommand command, CancellationToken cancellationToken)
    {
        var result = Quote.Create(command.Author, command.Text, _clock.UtcNow, command.OwnerId);
        if (!result.IsSuccess)
            return Result<int>.Fail(result.Error!.Message);

        var created = await _repository.AddAsync(result.Value!, cancellationToken);
        return Result<int>.Ok(created.Id);
    }
}
```

---

## Query + Read Model (Read Path)

```csharp
// ReadModels/QuoteListItem.cs — projection shaped for the list screen
public record QuoteListItem(int Id, string Author, string Text, DateTimeOffset CreatedAt);
```

```csharp
// Queries/GetQuotesQuery.cs
public record GetQuotesQuery(int Page, int Size);
```

```csharp
// Queries/GetQuotesQueryHandler.cs
public sealed class GetQuotesQueryHandler
{
    private readonly AppDbContext _context;
    public GetQuotesQueryHandler(AppDbContext context) => _context = context;

    // Projects directly to the screen shape — IsDeleted and OwnerId never leave the DB layer.
    public Task<List<QuoteListItem>> HandleAsync(GetQuotesQuery query, CancellationToken cancellationToken) =>
        _context.Quotes
            .Where(q => !q.IsDeleted)
            .OrderBy(q => q.Id)
            .Skip((query.Page - 1) * query.Size)
            .Take(query.Size)
            .Select(q => new QuoteListItem(q.Id, q.Author, q.Text, q.CreatedAt))
            .ToListAsync(cancellationToken);
}
```

---

## What Got Simpler

The read handler's `Select` clause **is** the response shape — `IsDeleted` and `OwnerId` are filtered
inside the SQL projection and structurally cannot leak to callers, so there is nothing to strip, map,
or guard on the way out.

---

## Before / After — Response Shape

### Before (full `Quote` entity returned directly)

```json
{
  "id": 1,
  "author": "Marcus Aurelius",
  "text": "The obstacle is the way.",
  "createdAt": "2026-05-31T07:00:00+00:00",
  "isDeleted": false,
  "ownerId": "1"
}
```

`isDeleted` and `ownerId` are internal fields — meaningless to the caller but leaked in every GET response.

### After (`QuoteListItem` read model)

```json
{
  "id": 1,
  "author": "Marcus Aurelius",
  "text": "The obstacle is the way.",
  "createdAt": "2026-05-31T07:00:00+00:00"
}
```

Only the four fields the screen needs. Impossible to accidentally expose `isDeleted` — the type doesn't have that property.

---

## Endpoint Before / After

### Before — validation + persistence inline in endpoint

```csharp
group.MapPost("/", async (CreateQuoteRequest request, ClaimsPrincipal user,
    IQuoteRepository repository, IClock clock, ...) =>
{
    var result = Quote.Create(request.Author, request.Text, clock.UtcNow, ownerId);
    if (!result.IsSuccess) return Results.ValidationProblem(...);
    var created = await repository.AddAsync(result.Value!, cancellationToken);
    return Results.Created($"/api/quotes/{created.Id}", created);   // full entity returned
});
```

### After — endpoint only handles HTTP shape and auth

```csharp
group.MapPost("/", async (CreateQuoteRequest request, ClaimsPrincipal user,
    CreateQuoteCommandHandler handler, ...) =>
{
    var result = await handler.HandleAsync(
        new CreateQuoteCommand(request.Author, request.Text, ownerId), cancellationToken);

    if (!result.IsSuccess) return Results.ValidationProblem(...);
    return Results.Created($"/api/quotes/{result.Value}", new { id = result.Value });
}).RequireAuthorization("can-edit-quotes");
```

---

## Separation Summary

| Concern | Before | After |
|---------|--------|-------|
| Validation | Inline in endpoint | `CreateQuoteCommandHandler` |
| Persistence | Inline in endpoint | `CreateQuoteCommandHandler` |
| Response shape | Full `Quote` entity | `QuoteListItem` projection |
| Internal fields exposed | `isDeleted`, `ownerId` visible | Structurally hidden |
| DB columns fetched on GET | All 6 columns | Only 4 (Id, Author, Text, CreatedAt) |

---

## Test Results

```
dotnet test QuotesApi.Tests/QuotesApi.Tests.csproj

Passed!  - Failed: 0, Passed: 38, Skipped: 0, Total: 38
```

### New CQRS handler tests (14 tests)

| Test | What it verifies |
|------|-----------------|
| `CreateQuote_ValidCommand_ReturnsSuccessWithNewId` | Happy path returns non-zero ID |
| `CreateQuote_BlankAuthor_ReturnsFailWithMessage` | Validation: empty author rejected |
| `CreateQuote_AuthorTooLong_ReturnsFailWithMessage` | Validation: author > 200 chars rejected |
| `CreateQuote_BlankText_ReturnsFailWithMessage` | Validation: whitespace-only text rejected |
| `CreateQuote_TextTooLong_ReturnsFailWithMessage` | Validation: text > 1000 chars rejected |
| `CreateQuote_ValidCommand_PersistsToDatabase` | Quote actually written to DB |
| `CreateQuote_SetsCreatedAtFromClock` | Clock injection wired correctly |
| `GetQuotes_EmptyDatabase_ReturnsEmptyList` | No rows → empty list |
| `GetQuotes_ReturnsOnlyNonDeletedQuotes` | Soft-delete filter works on read path |
| `GetQuotes_ResultsAreQuoteListItems_NotFullEntities` | Type is `QuoteListItem`, no `IsDeleted`/`OwnerId` |
| `GetQuotes_PaginationWorks` | Page/size skip logic correct across 3 pages |
| `GetQuoteById_ExistingId_ReturnsQuoteListItem` | Correct fields returned by ID |
| `GetQuoteById_NonExistentId_ReturnsNull` | Missing ID → null |
| `GetQuoteById_DeletedQuote_ReturnsNull` | Soft-deleted quotes invisible to read path |
