# Output – Day 12 Piece 2: EF vs Dapper

## 1. Test Run — All 43 Tests Pass

```
dotnet test "QuotesApi.Tests/QuotesApi.Tests.csproj"
```

```
Passed!  - Failed: 0, Passed: 43, Skipped: 0, Total: 43, Duration: 45 s
          - QuotesApi.Tests.dll (net10.0)
```

---

## 2. Dapper Tests — Timing Output

```
dotnet test "QuotesApi.Tests/QuotesApi.Tests.csproj"
  --filter "FullyQualifiedName~DapperTimingTests"
  --logger "console;verbosity=detailed"
```

```
═══════════════════════════════════════════════════════════════
  EF Core vs Dapper — GET /api/quotes (page=1, size=20, 500 rows in DB)
═══════════════════════════════════════════════════════════════
  Warmup iterations : 10
  Timed  iterations : 200

  EF SQL:
    SELECT "q"."Id", "q"."Author", "q"."Text", "q"."CreatedAt"
    FROM "Quotes" AS "q"
    WHERE NOT ("q"."IsDeleted")
    ORDER BY "q"."Id"
    LIMIT @__p_1 OFFSET @__p_0

  Dapper SQL:
    SELECT Id, Author, Text, CreatedAt
    FROM Quotes
    WHERE IsDeleted = 0
    ORDER BY Id
    LIMIT @Size OFFSET @Offset

  EF Core total  :    130 ms  |  avg   654.2 µs/call
  Dapper total   :     63 ms  |  avg   319.9 µs/call
  Dapper speedup : 2.04x

  RULE:
  Use Dapper on hot read paths where the query is fixed, the result is a
  DTO (no domain behaviour), and profiling shows EF's per-call overhead is
  measurable. Keep EF for writes, migrations, dynamic queries, and anything
  that benefits from the change tracker or first-level cache. On a DTO
  projection with EF's compiled-query cache warmed, the gap is typically
  < 20 µs — only reach for Dapper when you have profiling evidence, not
  as a default.
═══════════════════════════════════════════════════════════════

Passed QuotesApi.Tests.DapperTimingTests.TimingComparison_DapperVsEf_PrintsResultsToOutput [4 s]
Total tests: 1  |  Total time: 6.4890 Seconds
```

---

## 3. API — GET /api/quotes/dapper

```powershell
curl "http://localhost:5000/api/quotes/dapper?page=1&size=10"
```

```
StatusCode        : 200
StatusDescription : OK
Content           : [
  {
    "id": 1,
    "author": "Author 01",
    "text": "Quote 001 by Author 01 — the value of persistence is that it outlasts doubt.",
    "createdAt": "2026-05-31T02:50:00.4485401+00:00"
  },
  { "id": 2, "author": "Author 01", "text": "Quote 002 by Author 01 — ...", "createdAt": "..." },
  ...
  { "id": 10, "author": "Author 01", "text": "Quote 010 by Author 01 — ...", "createdAt": "..." }
]
RawContentLength  : 1662
```

---

## 4. API — GET /api/quotes/bench (Live EF vs Dapper Benchmark)

```powershell
(Invoke-WebRequest -UseBasicParsing `
  "http://localhost:5000/api/quotes/bench?page=1&size=20&iterations=200").Content |
  ConvertFrom-Json | ConvertTo-Json -Depth 4
```

```json
{
  "iterations": 200,
  "rowsPerPage": 20,
  "ef": {
    "totalMs": 15023,
    "avgMicros": 75118.9,
    "sql": "SELECT \"q\".\"Id\", \"q\".\"Author\", \"q\".\"Text\", \"q\".\"CreatedAt\"\r\nFROM \"Quotes\" AS \"q\"\r\nWHERE NOT (\"q\".\"IsDeleted\")\r\nORDER BY \"q\".\"Id\"\r\nLIMIT @__p_1 OFFSET @__p_0"
  },
  "dapper": {
    "totalMs": 2415,
    "avgMicros": 12078.6,
    "sql": "SELECT Id, Author, Text, CreatedAt\r\nFROM Quotes\r\nWHERE IsDeleted = 0\r\nORDER BY Id\r\nLIMIT @Size OFFSET @Offset"
  },
  "speedupFactor": 6.22,
  "rule": "Use Dapper on hot read paths where the query shape is fixed, the result is a DTO (no domain behaviour), and profiling shows EF's per-call overhead (expression-tree resolution, model-snapshot lookup, result-set shaping) is measurable. Keep EF for writes, migrations, dynamic/conditional queries, and any path that benefits from the change tracker or identity resolution. On a DTO projection with EF's compiled-query cache warm the delta is small (< 20 µs), so only reach for Dapper when you have profiling evidence — not as a default."
}
```

---

## 5. Summary Table

### Unit Tests — SQLite in-memory (ORM overhead only, no disk I/O)

| Handler     | Total (200 runs) | Avg / call |
|-------------|-----------------|------------|
| EF Core     | 130 ms          | 654 µs     |
| Dapper      | 63 ms           | 320 µs     |
| **Speedup** |                 | **2.04×**  |

### Live API — SQLite file on disk (real I/O + ORM overhead combined)

| Handler     | Total (200 runs) | Avg / call  |
|-------------|-----------------|-------------|
| EF Core     | 15,023 ms       | 75,119 µs   |
| Dapper      | 2,415 ms        | 12,079 µs   |
| **Speedup** |                 | **6.22×**   |

**Why the gap is bigger on disk:**
File I/O amplifies EF's per-call overhead (expression-tree resolution, model-snapshot traversal,
result-set shaper construction). In the in-memory test the database call costs ~0 µs so only
the ORM layer shows in the delta. On a real database, both factors compound — Dapper skips
all the ORM plumbing and takes the fast path straight to `IDataReader`.

---

## 6. The Rule

> Use Dapper on hot read paths where the query shape is fixed, the result is a DTO
> (no domain behaviour), and profiling shows EF's per-call overhead (expression-tree resolution,
> model-snapshot lookup, result-set shaping) is measurable. Keep EF for writes, migrations,
> dynamic/conditional queries, and any path that benefits from the change tracker or identity
> resolution. On a DTO projection with EF's compiled-query cache warm the delta is small
> (< 20 µs), so only reach for Dapper when you have profiling evidence — not as a default.
