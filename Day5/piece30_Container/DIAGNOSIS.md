# Piece 29 — Diagnose a Slow Endpoint Using Traces

## What Was Introduced

An **N+1 query** was intentionally added to `GET /api/quotes/` via `GetAllSlowAsync` in `QuoteRepository`.
Instead of one `SELECT * FROM Quotes` it first fetched all IDs, then issued one `SELECT` per ID in a loop.

```csharp
// BEFORE (slow N+1)
var ids = await _context.Quotes.Where(...).Select(q => q.Id).ToListAsync(ct);
foreach (var id in ids)
    quote = await _context.Quotes.FirstOrDefaultAsync(q => q.Id == id, ct);
```

---

## Before Trace (N+1)

```
Trace: 4b622a11774739ac...   Total: 18.3 ms   Spans: 8
  [ 18.3 ms]  GET /api/quotes/
  [ 10.1 ms]    list-quotes                       (custom activity, query.strategy=n+1)
  [  0.6 ms]      main  SELECT "q"."Id" FROM "Quotes" ...       ← 1 query to get IDs
  [  0.4 ms]      main  SELECT "q".* FROM "Quotes" WHERE Id=1   ← per-row query #1
  [  0.4 ms]      main  SELECT "q".* FROM "Quotes" WHERE Id=2   ← per-row query #2
  [  2.1 ms]      main  SELECT "q".* FROM "Quotes" WHERE Id=4   ← per-row query #3
  [  0.4 ms]      main  SELECT "q".* FROM "Quotes" WHERE Id=5   ← per-row query #4
  [  0.3 ms]      main  SELECT "q".* FROM "Quotes" WHERE Id=8   ← per-row query #5
```

**Span count: 8 (1 + N, where N = 5 quotes on the page)**

---

## After Trace (Fixed — single query)

```
Trace: f258ff01801a1e08...   Total: 4.4 ms   Spans: 3
  [  4.4 ms]  GET /api/quotes/
  [  2.1 ms]    list-quotes                       (query.strategy=single-query)
  [  1.2 ms]      main  SELECT "q".* FROM "Quotes" WHERE NOT IsDeleted LIMIT 5
```

**Span count: 3 — one DB round-trip regardless of page size**

---

## Diagnosis Note (≈100 words)

> This trace showed the slow span was the `list-quotes` activity inside `GET /api/quotes/`
> because of an N+1 query pattern: the repository first fetched a list of quote IDs (1 SQL),
> then issued a separate `SELECT` for each ID in a `foreach` loop — 5 extra round-trips for
> a page of 5 results. Each extra round-trip appears as its own EF Core span in Jaeger.
> At page-size 100 that becomes 101 DB calls per request.
> I fixed it by replacing `GetAllSlowAsync` with the existing `GetAllAsync`, which fetches
> all columns in one `WHERE NOT IsDeleted … LIMIT N` query, collapsing 8 spans to 3.

---

## The Fix

```csharp
// AFTER (efficient single query)
return await _context.Quotes
    .Where(q => !q.IsDeleted)
    .Skip((page - 1) * size)
    .Take(size)
    .ToListAsync(cancellationToken);
```

Changed `QuoteEndpoints.cs` to call `GetAllAsync` and tagged the activity with
`query.strategy = single-query` so future traces are self-documenting.

---

## Bonus — KQL Query to Find Slow Endpoints in App Insights

```kql
// Find all request traces where p95 duration exceeds 500ms, grouped by operation
requests
| where timestamp > ago(1h)
| summarize
    count         = count(),
    avg_ms        = avg(duration),
    p95_ms        = percentile(duration, 95),
    p99_ms        = percentile(duration, 99),
    error_count   = countif(success == false)
  by operation_Name, cloud_RoleName
| where p95_ms > 500
| order by p95_ms desc

// Drill into a specific slow operation and see its dependency (EF/SQL) spans
dependencies
| where timestamp > ago(1h)
| where operation_Name == "GET /api/quotes/"
| summarize
    call_count = count(),
    avg_ms     = avg(duration),
    p95_ms     = percentile(duration, 95)
  by name, type, target
| order by call_count desc
// High call_count on SELECT-by-PK rows with identical target = N+1 signal

// Correlate a slow request with all its child dependency spans (find N+1 in a trace)
let slow_ops =
    requests
    | where timestamp > ago(1h)
    | where operation_Name == "GET /api/quotes/" and duration > 100
    | project operation_Id;
dependencies
| where timestamp > ago(1h)
| where operation_Id in (slow_ops)
| project timestamp, operation_Id, name, duration, type
| order by operation_Id asc, timestamp asc
```

---

## Screenshots

Open Jaeger at **http://localhost:16686**:

1. Select service `QuotesApi`, operation `GET /api/quotes/`
2. **Before (N+1)** — find the trace with **8 spans**; the `list-quotes` span nests 6 DB child spans
3. **After (fixed)** — find a trace with **3 spans**; `list-quotes` nests a single DB span
