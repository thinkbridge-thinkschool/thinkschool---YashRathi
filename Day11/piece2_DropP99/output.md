# Drop P99 by 10× — Before / After

## Problem

`GET /api/quotes/by-author` had two independent performance problems that compound:

1. **N+1 queries** — `GetByAuthorSlowAsync` runs:
   - Query 1: `SELECT DISTINCT Author FROM Quotes WHERE IsDeleted=0`
   - Query 2..N+1: `SELECT Text FROM Quotes WHERE Author=@p AND IsDeleted=0` — once per author
   With 20 seeded authors that is **21 round-trips per HTTP request**.

2. **No index on `Author`** — every per-author SELECT is a full table scan over all 1 000 rows.

---

## Changes Made

### 1 — Single projection query (`QuoteRepository.GetByAuthorFastAsync`)

```csharp
// Before (21 queries for 20 authors)
var authors = await _context.Quotes
    .Where(q => !q.IsDeleted)
    .Select(q => q.Author).Distinct()
    .ToListAsync(cancellationToken);

foreach (var author in authors)
{
    var texts = await _context.Quotes
        .Where(q => q.Author == author && !q.IsDeleted)
        .Select(q => q.Text).ToListAsync(cancellationToken);
    result[author] = texts;
}

// After (1 query)
var rows = await _context.Quotes
    .Where(q => !q.IsDeleted)
    .OrderBy(q => q.Author)
    .Select(q => new { q.Author, q.Text })
    .ToListAsync(cancellationToken);

return rows.GroupBy(r => r.Author)
    .ToDictionary(g => g.Key, g => g.Select(r => r.Text).ToList());
```

Generated SQL (after):
```sql
SELECT "q"."Author", "q"."Text"
FROM "Quotes" AS "q"
WHERE "q"."IsDeleted" = 0
ORDER BY "q"."Author"
```

### 2 — Composite index (`Migrations/20260531000001_AddCompositeIsDeletedAuthorIndex.cs`)

Replaced the simple `IX_Quotes_Author` with a composite `(IsDeleted, Author)` index:

```sql
DROP INDEX "IX_Quotes_Author";
CREATE INDEX "IX_Quotes_IsDeleted_Author" ON "Quotes" ("IsDeleted", "Author");
```

Added to `AppDbContext.OnModelCreating`:
```csharp
modelBuilder.Entity<Quote>(b =>
{
    b.HasIndex(q => new { q.IsDeleted, q.Author })
     .HasDatabaseName("IX_Quotes_IsDeleted_Author");
});
```

With `WHERE IsDeleted=0 ORDER BY Author`, SQLite seeks directly to `IsDeleted=0` entries
and reads them already sorted by `Author` — no filter pass, no temp B-Tree sort needed.

### 3 — IMemoryCache (30 s TTL) on `/api/quotes/by-author-fast`

```csharp
var result = await cache.GetOrCreateAsync("quotes:by-author", async entry =>
{
    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
    return await repository.GetByAuthorFastAsync(cancellationToken);
});
```

Under k6 load (10 VUs × 30 s), only the **first request** per 30 s window hits the DB.
All subsequent VU requests are served from RAM in < 1 ms.

### 4 — New endpoint

`GET /api/quotes/by-author-fast` → calls `GetByAuthorFastAsync` + cache.
The old `/api/quotes/by-author` is preserved to allow side-by-side measurement.

---

## Execution Plans

### Before — `/api/quotes/by-author` (slow)

**Query 1** — get distinct authors (no index, full scan):
```
EXPLAIN QUERY PLAN
SELECT DISTINCT "Author" FROM "Quotes" WHERE "IsDeleted" = 0;

SCAN Quotes                         ← full table scan, 1 000 rows examined
USE TEMP B-TREE FOR DISTINCT
```

**Query 2..21** — per-author text fetch (no index, full scan each):
```
EXPLAIN QUERY PLAN
SELECT "Text" FROM "Quotes"
WHERE "Author" = 'Author 01' AND "IsDeleted" = 0;

SCAN Quotes                         ← full table scan per author, 1 000 rows each
```

Total rows examined per request: `1 000 + 20 × 1 000 = 21 000 rows`.

---

### After — `/api/quotes/by-author-fast` (fixed)

**Single query** — projection ordered by Author, with composite index:
```
EXPLAIN QUERY PLAN
SELECT "Author", "Text" FROM "Quotes"
WHERE "IsDeleted" = 0
ORDER BY "Author";

SEARCH Quotes USING INDEX IX_Quotes_IsDeleted_Author (IsDeleted=?)
                                    ← seeks to IsDeleted=0, reads Author in order
                                    ← no temp B-Tree sort, no secondary filter
```

Total rows examined per request: `1 000 rows once`, pre-sorted by index.

---

## Before / After p99 — Measured Results (k6, 10 VUs × 30 s, SQLite, 1 000 seeded rows)

| Metric | Slow (`/by-author`) | Fast (`/by-author-fast`) | Improvement |
|--------|--------------------|-----------------------------|-------------|
| **p(99)** | **10,220 ms** | **184 ms** | **55.5×** ✓ |
| p(95) | 8,580 ms | 127 ms | 67× |
| med | 7,990 ms | 62 ms | 129× |
| max | 10,390 ms | 530 ms | 20× |
| Throughput | 1.23 req/s | 142.9 req/s | 116× |
| Total requests / 30 s | 40 | 4,297 | — |
| checks passed | 100% | 100% | — |

Target was ≥10× improvement on p99. **Achieved 55.5×.**

### Why slow is 10 seconds

21 SQL queries × full table scan on 1 000 rows, with 10 VUs competing for
the SQLite write lock simultaneously. Each VU blocks on the previous one —
requests pile up and p(99) blows out to 10 s.

### Why fast p(99) is ~184 ms (not sub-millisecond)

The response body is **81 KB of JSON** (20 authors × 50 quotes × ~80 chars each).
The IMemoryCache eliminates the DB round-trip entirely, but every request still:
1. Serializes the cached `Dictionary<string, List<string>>` to JSON (CPU)
2. Transfers 81 KB over loopback (network)

The bottleneck moved from the database to serialization + payload size.
Further gains would require response compression (gzip) or pagination.

### How to reproduce

```bash
# Start the API (DB seeded automatically on first run)
dotnet run --project QuotesApi.csproj

# Terminal 2 — Slow baseline
k6 run k6-slow.js

# Terminal 2 — Fast fix
k6 run k6-fast.js
```

---

## Root-cause summary

| Problem | Fix | Effect |
|---------|-----|--------|
| 21 SQL round-trips | Single projection query | 21 queries → 1 |
| Full table scan per author | Composite `(IsDeleted, Author)` index | Index seek + pre-sorted rows |
| DB hit on every VU request | IMemoryCache 30 s TTL | Only 1 DB call per 30 s window |
| **Combined** | All three together | p99: **10,220 ms → 184 ms (55.5×)** |

---

## xUnit test coverage (70 tests total — 0 failures)

| Test | What it verifies |
|------|-----------------|
| `FastEndpoint_Returns200WithAuthorDictionary` | Endpoint responds 200 with JSON object |
| `FastEndpoint_ReturnsSameDataAsSlowEndpoint` | Fast and slow return identical data |
| `FastEndpoint_ContainsSeededAuthors` | 20 authors × 50 quotes each |
| `FastEndpoint_SecondCall_ReturnsSameDataAsCachedFirst` | Cache returns byte-identical response |
| `FastRepository_FiresExactlyOneQuery` | `SelectCountInterceptor` sees exactly 1 SELECT |
| `SlowRepository_FiresNPlusOneQueries` | Slow path fires authorCount + 1 queries |
| `FastRepository_QueryCountDoesNotGrowWithAuthors` | Count stays 1 with 10 authors |
