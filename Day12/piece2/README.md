# Day 12 – Piece 2: When to Reach for Dapper

## Problem Statement

> EF is the default; Dapper earns its place on hot read paths. Reimplement your fastest-needed read query with Dapper, compare the SQL + timing to the EF version, and write the rule you'd give a teammate for when to drop to Dapper.

---

## What Was Built

The hottest read path in this API is `GET /api/quotes` — a paginated DTO projection over 1 000 rows.
It was reimplemented side-by-side using Dapper, with:

| Added | Purpose |
|-------|---------|
| `Queries/GetQuotesDapperQueryHandler.cs` | Dapper version of the paginated list query |
| `GET /api/quotes/dapper` | Live endpoint backed by the Dapper handler |
| `GET /api/quotes/bench` | Benchmark endpoint — runs both handlers 200× and returns timing JSON |
| `QuotesApi.Tests/DapperTimingTests.cs` | 4 correctness tests + 1 timing comparison test |

---

## Both Implementations

### EF Core — `Queries/GetQuotesQueryHandler.cs`

```csharp
public Task<List<QuoteListItem>> HandleAsync(GetQuotesQuery query, CancellationToken cancellationToken) =>
    _context.Quotes
        .Where(q => !q.IsDeleted)
        .OrderBy(q => q.Id)
        .Skip((query.Page - 1) * query.Size)
        .Take(query.Size)
        .Select(q => new QuoteListItem(q.Id, q.Author, q.Text, q.CreatedAt))
        .ToListAsync(cancellationToken);
```

**SQL sent (SQLite dialect):**
```sql
SELECT "q"."Id", "q"."Author", "q"."Text", "q"."CreatedAt"
FROM "Quotes" AS "q"
WHERE NOT ("q"."IsDeleted")
ORDER BY "q"."Id"
LIMIT @__p_1 OFFSET @__p_0
```

---

### Dapper — `Queries/GetQuotesDapperQueryHandler.cs`

```csharp
public async Task<IReadOnlyList<QuoteListItem>> HandleAsync(
    GetQuotesQuery query, CancellationToken cancellationToken)
{
    var conn = _context.Database.GetDbConnection();
    if (conn.State != ConnectionState.Open)
        await _context.Database.OpenConnectionAsync(cancellationToken);

    var rows = await conn.QueryAsync<QuoteRow>(
        "SELECT Id, Author, Text, CreatedAt FROM Quotes WHERE IsDeleted = 0 ORDER BY Id LIMIT @Size OFFSET @Offset",
        new { Size = query.Size, Offset = (query.Page - 1) * query.Size });

    return rows
        .Select(r => new QuoteListItem(
            (int)r.Id, r.Author, r.Text,
            DateTimeOffset.Parse(r.CreatedAt, null, DateTimeStyles.RoundtripKind)))
        .ToList();
}
```

**SQL sent verbatim:**
```sql
SELECT Id, Author, Text, CreatedAt
FROM Quotes
WHERE IsDeleted = 0
ORDER BY Id
LIMIT @Size OFFSET @Offset
```

> **SQLite note:** The ADO.NET driver surfaces `INTEGER` as `long` and `DateTimeOffset` as ISO-8601 `string`.
> A private `QuoteRow` mutable class is used so Dapper's property-setter path works with native ADO.NET types,
> then the result is projected to `QuoteListItem`.

---

## Timing Comparison

### Unit Tests — SQLite in-memory (isolates ORM overhead, no disk I/O)

| Handler     | Total (200 runs) | Avg / call  |
|-------------|-----------------|-------------|
| EF Core     | 130 ms          | 654 µs      |
| Dapper      | 63 ms           | 320 µs      |
| **Speedup** |                 | **2.04×**   |

### Live API — SQLite file on disk (real I/O + ORM overhead combined)

| Handler     | Total (200 runs) | Avg / call  |
|-------------|-----------------|-------------|
| EF Core     | 15,023 ms       | 75,119 µs   |
| Dapper      | 2,415 ms        | 12,079 µs   |
| **Speedup** |                 | **6.22×**   |

The gap is larger on the live run because file I/O compounds EF's per-call overhead
(expression-tree resolution, model-snapshot traversal, result-set shaper construction).
Dapper sends the literal SQL string directly and does a single-pass `IDataReader` mapping —
no expression tree, no model snapshot, no change-tracker bookkeeping even for a projection.

The SQL plans are logically identical — every millisecond of difference is pure ORM plumbing.

---

## The Rule

> **Use Dapper on hot read paths where the query shape is fixed, the result is a DTO
> (no domain behaviour), and profiling shows EF's per-call overhead is measurable.**
>
> Keep EF for writes, migrations, dynamic/conditional queries, and any path that benefits from
> the change tracker or identity resolution. On a DTO projection with EF's compiled-query cache
> warm, the delta is typically < 20 µs — only reach for Dapper when you have profiling evidence,
> not as a default.

---

## Project Structure

```
piece2/
├── Queries/
│   ├── GetQuotesQuery.cs                  ← shared input record (page, size)
│   ├── GetQuotesQueryHandler.cs           ← EF Core DTO projection
│   ├── GetQuotesDapperQueryHandler.cs     ← Dapper raw-SQL version  ← NEW
│   ├── GetQuoteByIdQuery.cs
│   └── GetQuoteByIdQueryHandler.cs
├── Endpoints/
│   └── QuoteEndpoints.cs                  ← /dapper + /bench added  ← UPDATED
├── Extensions/
│   └── InfrastructureExtensions.cs        ← Dapper handler registered  ← UPDATED
├── QuotesApi.Tests/
│   ├── CqrsHandlerTests.cs                ← existing 38 tests
│   └── DapperTimingTests.cs               ← 5 new tests  ← NEW
├── QuotesApi.csproj                       ← Dapper 2.1.28 added  ← UPDATED
├── README.md
└── output.md
```

---

## How to Run

### Run all tests

```powershell
cd "C:\Users\LENOVO\OneDrive\Desktop\Thinkschool\Day12\piece2"
dotnet test "QuotesApi.Tests/QuotesApi.Tests.csproj"
```

Expected: **43 tests, 0 failures**

### Run Dapper tests with timing output

```powershell
dotnet test "QuotesApi.Tests/QuotesApi.Tests.csproj" `
  --filter "FullyQualifiedName~DapperTimingTests" `
  --logger "console;verbosity=detailed"
```

### Start the API

```powershell
dotnet run --project QuotesApi.csproj
```

### Hit the new endpoints (in a second terminal)

```powershell
# Dapper read — same JSON shape as GET /api/quotes
Invoke-WebRequest -UseBasicParsing "http://localhost:5000/api/quotes/dapper?page=1&size=10"

# Live EF vs Dapper benchmark — formatted JSON
(Invoke-WebRequest -UseBasicParsing `
  "http://localhost:5000/api/quotes/bench?page=1&size=20&iterations=200").Content |
  ConvertFrom-Json | ConvertTo-Json -Depth 4
```

---

## Test Coverage

### `DapperTimingTests` — 5 tests

| Test | What it verifies |
|------|-----------------|
| `Dapper_ReturnsIdenticalResultsToEf` | Dapper and EF return the same rows in the same order |
| `Dapper_PaginationMatchesEf` | Page/size offsets match EF across 5 pages |
| `Dapper_ExcludesSoftDeletedRows` | `IsDeleted = 0` filter works in raw SQL |
| `Dapper_EmptyDatabase_ReturnsEmptyList` | Empty DB returns empty list |
| `TimingComparison_DapperVsEf_PrintsResultsToOutput` | 200 timed iterations, prints µs/call and speedup |
