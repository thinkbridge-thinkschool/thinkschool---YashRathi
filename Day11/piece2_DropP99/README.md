# Day 11 — Piece 1: Profiling a Slow Endpoint

## Problem Statement

Profile a deliberately slow endpoint on the existing QuotesAPI.

- Add an endpoint that exhibits the **N+1 query problem** over authors and quotes
- Load test it with **k6** to capture p50/p99 latency under concurrent users
- Capture the **SQL queries** EF Core emits
- Run **EXPLAIN QUERY PLAN** on each query to see the execution plan
- State the two biggest performance problems found

---

## What I Built

### Slow Endpoint

`GET /api/quotes/by-author`

Implemented `GetByAuthorSlowAsync()` in `QuoteRepository` which deliberately fires N+1 queries:

**Step 1 — one query to get all distinct authors:**
```sql
SELECT DISTINCT "q"."Author"
FROM "Quotes" AS "q"
WHERE NOT ("q"."IsDeleted")
```

**Step 2 — one query per author to get their quotes (×20):**
```sql
SELECT "q"."Text"
FROM "Quotes" AS "q"
WHERE "q"."Author" = @author AND NOT ("q"."IsDeleted")
```

**Total: 21 DB round trips per HTTP request.**

### Seed Data

Seeded **20 authors × 50 quotes = 1000 rows** in `Program.cs` on startup so the N+1 cost is clearly visible under load.

---

## Files Changed

| File | Change |
|---|---|
| `Repositories/IQuoteRepository.cs` | Added `GetByAuthorSlowAsync()` to interface |
| `Repositories/QuoteRepository.cs` | Implemented N+1 author → quotes pattern |
| `Endpoints/QuoteEndpoints.cs` | Mapped `GET /api/quotes/by-author` |
| `Program.cs` | Added 1000-row seed block on startup |
| `k6-slow.js` | k6 load test script (10 VUs, 30s) |

---

## How to Run

### 1. Delete old DB and build
```powershell
Remove-Item "quotes.db" -Force -ErrorAction SilentlyContinue
dotnet build --configuration Debug
```

### 2. Start the API (Terminal 1)
```powershell
dotnet run --launch-profile http
```
Wait for `Now listening on: http://localhost:5000`. The 1000-row seed runs automatically.

### 3. Verify the endpoint (Terminal 2)
```powershell
Invoke-WebRequest -Uri "http://localhost:5000/api/quotes/by-author" -UseBasicParsing | Select-Object StatusCode
```

### 4. Run k6 load test (Terminal 2)
```powershell
k6 run k6-slow.js
```

### 5. Run EXPLAIN QUERY PLAN (Terminal 2)
```powershell
# Query 1 — SELECT DISTINCT Author
sqlite3 "quotes.db" "EXPLAIN QUERY PLAN SELECT DISTINCT Author FROM Quotes WHERE NOT IsDeleted;"

# Query 2 — per-author SELECT
sqlite3 "quotes.db" "EXPLAIN QUERY PLAN SELECT Text FROM Quotes WHERE Author = 'Author 01' AND NOT IsDeleted;"

# Confirm no indexes exist
sqlite3 "quotes.db" "SELECT name, sql FROM sqlite_master WHERE type='index' AND tbl_name='Quotes';"

# Row count
sqlite3 "quotes.db" "SELECT COUNT(*) as total, COUNT(DISTINCT Author) as authors FROM Quotes WHERE NOT IsDeleted;"
```

---

## Baseline Results

**k6 — 10 VUs, 30 seconds, 1000 rows, SQLite**

| Metric | Value |
|---|---|
| p50 (median) | **8.84 s** |
| p95 | 9.70 s |
| p99 | **10.64 s** |
| max | 11.20 s |
| Throughput | 1.16 req/s |
| Total requests | 40 |
| Success rate | 100% |

---

## Execution Plan

**Query 1 — SELECT DISTINCT Author:**
```
QUERY PLAN
|--SCAN Quotes                    ← full table scan (no index on IsDeleted)
`--USE TEMP B-TREE FOR DISTINCT   ← allocates a temp B-tree to deduplicate 1000 Author values
```

**Query 2 — SELECT Text WHERE Author = @author:**
```
QUERY PLAN
`--SCAN Quotes                    ← full table scan (no index on Author)
```

No `SEARCH` node appears in either plan. SQLite reads all 1000 rows for every query because there are no indexes on `Author` or `IsDeleted`.

**Indexes on Quotes table:**
```
(empty — only the implicit PK on Id exists)
```

---

## Two Biggest Problems Found

### Problem 1 — N+1 Queries (dominant cost)

Every HTTP request fires **21 separate database round trips**:
- 1 `SELECT DISTINCT Author` query
- 20 `SELECT Text WHERE Author = @author` queries (one per author)

Under 10 concurrent users this means ~210 database calls per second for a single endpoint. The fix is to replace the loop with a single query using `GROUP BY` or a projection that fetches all authors and their quotes in one shot.

### Problem 2 — Missing Index on `Quotes.Author`

Both queries show `SCAN Quotes` — a full read of all 1000 rows regardless of how many rows the query needs to return. Each of the 20 per-author queries scans the entire table to return just 50 rows.

A composite index on `(Author, IsDeleted)` would change the plan from `SCAN` to `SEARCH`, letting SQLite jump directly to the matching rows and cutting logical reads by ~95%.
