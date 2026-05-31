# Day 12 — Piece 1: Read Models + CQRS-lite

## Objective

Split one feature into a **write model** (normalized, validated) and a **read model**
(denormalized, projection-shaped for the screen). No event sourcing — just separate query
and command paths.

---

## Problem

Before the split, the `QuoteEndpoints` class mixed every concern into one place:

- `POST /api/quotes` ran `Quote.Create()` validation, called `repository.AddAsync`, and returned the full `Quote` entity including internal fields (`isDeleted`, `ownerId`).
- `GET /api/quotes` loaded the full `Quote` entity from the DB and serialized all 6 columns — even the ones the caller never uses.

**Two specific issues this causes:**

1. **Internal fields leak to callers** — `isDeleted` and `ownerId` appear in every GET response even though no screen needs them.
2. **Validation and persistence are glued to the HTTP layer** — impossible to test without spinning up the full request pipeline.

---

## Solution

### Write Path — Command + Handler

A `CreateQuoteCommand` carries the raw input. `CreateQuoteCommandHandler` owns validation
(via `Quote.Create`) and persistence. The endpoint is reduced to auth + routing + HTTP shape.

```
POST /api/quotes
    → CreateQuoteCommand(Author, Text, OwnerId)
        → CreateQuoteCommandHandler.HandleAsync()
            → Quote.Create()       ← domain validation
            → repository.AddAsync  ← persistence
            → Result<int>          ← just the new ID
```

### Read Path — Query + Read Model

`QuoteListItem` is a flat record with only the four fields the list screen needs.
`GetQuotesQueryHandler` projects directly into this shape inside the SQL `SELECT` —
`IsDeleted` and `OwnerId` are never loaded.

```
GET /api/quotes?page=1&size=10
    → GetQuotesQuery(Page, Size)
        → GetQuotesQueryHandler.HandleAsync()
            → SELECT Id, Author, Text, CreatedAt  ← IsDeleted/OwnerId never fetched
            → List<QuoteListItem>
```

---

## Key Files

### New files added

| File | Role |
|------|------|
| `Commands/CreateQuoteCommand.cs` | Write-side input record |
| `Commands/CreateQuoteCommandHandler.cs` | Validation + persistence, returns `Result<int>` |
| `Queries/GetQuotesQuery.cs` | Read-side input record (page, size) |
| `Queries/GetQuotesQueryHandler.cs` | Projects `Quote` → `QuoteListItem` |
| `Queries/GetQuoteByIdQuery.cs` | Read-side input record (id) |
| `Queries/GetQuoteByIdQueryHandler.cs` | Projects single quote → `QuoteListItem` |
| `ReadModels/QuoteListItem.cs` | Screen-shaped DTO: `(Id, Author, Text, CreatedAt)` |
| `QuotesApi.Tests/CqrsHandlerTests.cs` | 14 unit tests for all three handlers |

### Modified files

| File | Change |
|------|--------|
| `Endpoints/QuoteEndpoints.cs` | `GET /`, `GET /{id}`, `POST /` wired to handlers |
| `Extensions/InfrastructureExtensions.cs` | Handlers registered in DI (scoped) |
| `Quotes.Tests.Integration/QuoteReadTests.cs` | `QuoteDto` updated to match new read model shape |

---

## What Got Simpler

The read handler's `Select` clause **is** the response shape — `IsDeleted` and `OwnerId` are
filtered inside the SQL projection and structurally cannot leak to callers, so there is nothing
to strip, map, or guard on the way out.

---

## Project Structure

```
piece1_CQRSlite/
├── Commands/
│   ├── CreateQuoteCommand.cs          ← write-side input record
│   └── CreateQuoteCommandHandler.cs   ← validation + persistence → Result<int>
├── Queries/
│   ├── GetQuotesQuery.cs              ← page + size input
│   ├── GetQuotesQueryHandler.cs       ← SELECT Id,Author,Text,CreatedAt (paged)
│   ├── GetQuoteByIdQuery.cs           ← id input
│   └── GetQuoteByIdQueryHandler.cs    ← SELECT Id,Author,Text,CreatedAt WHERE Id=@id
├── ReadModels/
│   └── QuoteListItem.cs               ← record(Id, Author, Text, CreatedAt)
├── Endpoints/
│   └── QuoteEndpoints.cs              ← wired to handlers; DELETE still uses IQuoteRepository
├── Extensions/
│   └── InfrastructureExtensions.cs    ← handlers registered as scoped
├── QuotesApi.Tests/
│   └── CqrsHandlerTests.cs            ← 14 tests: 7 command, 4 list query, 3 by-id query
└── output.md                          ← exercise answer
```

---

## How to Run

### 1. Build and test
```powershell
cd "C:\Users\LENOVO\OneDrive\Desktop\Thinkschool\Day12\piece1_CQRSlite"

dotnet build
dotnet test QuotesApi.Tests/QuotesApi.Tests.csproj
dotnet test Quotes.Tests.Unit/Quotes.Tests.Unit.csproj
```

Expected: **38 + 46 = 84 tests, 0 failures**

Run only the new CQRS tests:
```powershell
dotnet test QuotesApi.Tests/QuotesApi.Tests.csproj --filter "FullyQualifiedName~CqrsHandlerTests"
```

### 2. Start the API
```powershell
dotnet run --project QuotesApi.csproj
```

API starts at `http://localhost:5000`. SQLite DB is auto-created and seeded with 20 authors × 50 quotes = 1 000 rows.

### 3. Smoke test (open a second terminal)

**Login:**
```powershell
$login = Invoke-RestMethod -Method Post -Uri "http://localhost:5000/api/auth/login" `
  -ContentType "application/json" `
  -Body '{"email":"test@example.com","password":"password123"}'

$token = $login.accessToken
```

**List quotes — read model (no isDeleted / ownerId):**
```powershell
Invoke-RestMethod -Uri "http://localhost:5000/api/quotes?page=1&size=5"
```

**Create a quote — command handler:**
```powershell
$result = Invoke-RestMethod -Method Post -Uri "http://localhost:5000/api/quotes" `
  -ContentType "application/json" `
  -Headers @{ Authorization = "Bearer $token" } `
  -Body '{"author":"Marcus Aurelius","text":"The obstacle is the way."}'

$result   # returns { id = <new_id> }
```

**Get by ID — read model:**
```powershell
Invoke-RestMethod -Uri "http://localhost:5000/api/quotes/$($result.id)"
```

**Validation failure (blank author → 400):**
```powershell
try {
  Invoke-RestMethod -Method Post -Uri "http://localhost:5000/api/quotes" `
    -ContentType "application/json" `
    -Headers @{ Authorization = "Bearer $token" } `
    -Body '{"author":"","text":"Some text."}'
} catch {
  $_.Exception.Response.StatusCode   # BadRequest
}
```

---

## Test Coverage

### `CqrsHandlerTests` — 14 new tests

| Test | Handler | What it verifies |
|------|---------|-----------------|
| `CreateQuote_ValidCommand_ReturnsSuccessWithNewId` | Command | Happy path returns non-zero ID |
| `CreateQuote_BlankAuthor_ReturnsFailWithMessage` | Command | Empty author rejected |
| `CreateQuote_AuthorTooLong_ReturnsFailWithMessage` | Command | Author > 200 chars rejected |
| `CreateQuote_BlankText_ReturnsFailWithMessage` | Command | Whitespace-only text rejected |
| `CreateQuote_TextTooLong_ReturnsFailWithMessage` | Command | Text > 1000 chars rejected |
| `CreateQuote_ValidCommand_PersistsToDatabase` | Command | Row actually written to DB |
| `CreateQuote_SetsCreatedAtFromClock` | Command | Clock injection wired correctly |
| `GetQuotes_EmptyDatabase_ReturnsEmptyList` | List query | No rows → empty list |
| `GetQuotes_ReturnsOnlyNonDeletedQuotes` | List query | Soft-delete filter works |
| `GetQuotes_ResultsAreQuoteListItems_NotFullEntities` | List query | Type is `QuoteListItem`, no `IsDeleted`/`OwnerId` |
| `GetQuotes_PaginationWorks` | List query | Page/size skip correct across 3 pages |
| `GetQuoteById_ExistingId_ReturnsQuoteListItem` | ById query | Correct fields returned |
| `GetQuoteById_NonExistentId_ReturnsNull` | ById query | Missing ID → null |
| `GetQuoteById_DeletedQuote_ReturnsNull` | ById query | Soft-deleted quotes invisible to read path |

---

## Separation of Concerns — Summary

| Concern | Before | After |
|---------|--------|-------|
| Validation | Inline in endpoint | `CreateQuoteCommandHandler` |
| Persistence | Inline in endpoint | `CreateQuoteCommandHandler` |
| Response shape | Full `Quote` entity (6 fields) | `QuoteListItem` (4 fields) |
| Internal fields exposed | `isDeleted`, `ownerId` in every GET | Structurally impossible |
| DB columns fetched on GET | All 6 columns | Only 4 (Id, Author, Text, CreatedAt) |
| Testability | Requires full HTTP pipeline | Plain `new Handler(ctx, clock)` |
