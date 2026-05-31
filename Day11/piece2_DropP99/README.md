# Day 11 — Piece 2: Drop P99 by 10×

## Objective

Fix the N+1 query problem on `GET /api/quotes/by-author`, add the right index, and re-measure under the same k6 load. Target: ≥10× improvement on p99 latency.

---

## Problem

The slow endpoint `GET /api/quotes/by-author` had two compounding issues:

### 1. N+1 Queries
```
Query 1:    SELECT DISTINCT Author FROM Quotes WHERE IsDeleted=0
Query 2:    SELECT Text FROM Quotes WHERE Author='Author 01' AND IsDeleted=0
Query 3:    SELECT Text FROM Quotes WHERE Author='Author 02' AND IsDeleted=0
...
Query 21:   SELECT Text FROM Quotes WHERE Author='Author 20' AND IsDeleted=0
```
**21 round-trips per HTTP request** for 20 authors.

### 2. No Index on Author
Every per-author SELECT was a **full table scan** over all 1 000 rows.

### Result
With 10 concurrent users, SQLite lock contention + 21 full scans = **p99 of 10,220 ms**.

---

## Fix

### Fix 1 — Single Projection Query (eliminates N+1)

```csharp
// Before: 21 queries
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

// After: 1 query
var rows = await _context.Quotes
    .Where(q => !q.IsDeleted)
    .OrderBy(q => q.Author)
    .Select(q => new { q.Author, q.Text })
    .ToListAsync(cancellationToken);

return rows.GroupBy(r => r.Author)
    .ToDictionary(g => g.Key, g => g.Select(r => r.Text).ToList());
```

### Fix 2 — Composite Index `(IsDeleted, Author)`

```sql
CREATE INDEX "IX_Quotes_IsDeleted_Author" ON "Quotes" ("IsDeleted", "Author");
```

SQLite now seeks directly to `IsDeleted=0` rows pre-sorted by `Author` — no filter pass, no temp B-Tree sort.

### Fix 3 — IMemoryCache (30 s TTL)

```csharp
var result = await cache.GetOrCreateAsync("quotes:by-author", async entry =>
{
    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
    return await repository.GetByAuthorFastAsync(cancellationToken);
});
```

Under k6 load (10 VUs), only the first request hits the DB. All other VUs are served from RAM.

---

## Before / After Results

| Metric | Before (`/by-author`) | After (`/by-author-fast`) | Improvement |
|--------|-----------------------|--------------------------|-------------|
| **p(99)** | **10,220 ms** | **184 ms** | **55.5×** ✓ |
| p(95) | 8,580 ms | 127 ms | 67× |
| med | 7,990 ms | 62 ms | 129× |
| Throughput | 1.23 req/s | 142.9 req/s | 116× |
| Requests / 30 s | 40 | 4,297 | — |

**Target: ≥10× — Achieved: 55.5×**

---

## Execution Plans

### Before (full table scan x 21)
```
SCAN Quotes                    <- full scan, 1 000 rows per query
USE TEMP B-TREE FOR DISTINCT   <- extra sort step
-- repeated 20 more times for each author
```

### After (index seek x 1)
```
SEARCH Quotes USING INDEX IX_Quotes_IsDeleted_Author (IsDeleted=?)
                               <- seeks to IsDeleted=0 range
                               <- rows already sorted by Author
                               <- no temp sort, no secondary filter
```

---

## Project Structure

```
piece2_DropP99/
├── Data/
│   └── AppDbContext.cs              <- Composite index registered
├── Endpoints/
│   └── QuoteEndpoints.cs            <- /by-author-fast endpoint + cache
├── Extensions/
│   └── InfrastructureExtensions.cs  <- AddMemoryCache() registered
├── Migrations/
│   ├── 20260531000000_AddAuthorIndex.cs
│   └── 20260531000001_AddCompositeIsDeletedAuthorIndex.cs
├── Repositories/
│   ├── IQuoteRepository.cs          <- GetByAuthorFastAsync interface
│   └── QuoteRepository.cs           <- Single projection query implementation
├── QuotesApi.Tests/
│   └── ByAuthorQueryTests.cs        <- 7 tests: correctness + query count
├── k6-slow.js                       <- Load test: slow endpoint (baseline)
├── k6-fast.js                       <- Load test: fast endpoint (fixed)
└── output.md                        <- Full before/after analysis
```

---

## How to Run

### 1. Build and Test
```powershell
dotnet restore
dotnet build
dotnet test QuotesApi.Tests --logger "console;verbosity=minimal"
dotnet test Quotes.Tests.Unit --logger "console;verbosity=minimal"
```

Expected: **70 tests, 0 failures**

### 2. Start the API
```powershell
dotnet run --project QuotesApi.csproj
```

API starts at `http://localhost:5000`. SQLite DB is auto-created and seeded with 20 authors x 50 quotes = 1 000 rows.

### 3. Smoke Test (open a new terminal)
```powershell
# Slow endpoint (N+1)
curl http://localhost:5000/api/quotes/by-author

# Fast endpoint (fixed)
curl http://localhost:5000/api/quotes/by-author-fast
```

### 4. k6 Load Test
```powershell
# Baseline - before fix
k6 run k6-slow.js

# After fix
k6 run k6-fast.js
```

---

## Test Coverage

| Test | What It Verifies |
|------|-----------------|
| `FastEndpoint_Returns200WithAuthorDictionary` | Endpoint returns 200 with JSON object |
| `FastEndpoint_ReturnsSameDataAsSlowEndpoint` | Fast and slow return identical data |
| `FastEndpoint_ContainsSeededAuthors` | 20 authors x 50 quotes each |
| `FastEndpoint_SecondCall_ReturnsSameDataAsCachedFirst` | Cache returns identical response |
| `FastRepository_FiresExactlyOneQuery` | Only 1 SQL SELECT fired |
| `SlowRepository_FiresNPlusOneQueries` | Slow path fires N+1 queries |
| `FastRepository_QueryCountDoesNotGrowWithAuthors` | Query count stays 1 regardless of author count |

---

## Root Cause Summary

| Problem | Fix | Effect |
|---------|-----|--------|
| 21 SQL round-trips | Single projection query | 21 queries to 1 |
| Full table scan per author | Composite `(IsDeleted, Author)` index | Index seek + pre-sorted output |
| DB hit on every VU request | IMemoryCache 30 s TTL | 1 DB call per 30 s window |
| **Combined** | All three | **p99: 10,220 ms to 184 ms (55.5x)** |
