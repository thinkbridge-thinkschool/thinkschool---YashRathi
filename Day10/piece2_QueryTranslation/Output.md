# Day 10 — Piece 2: Query Translation + Projections
## Demo Output & Test Results

---

## Test Results

```
dotnet test EFCoreDemo.Tests/EFCoreDemo.Tests.csproj --logger "console;verbosity=minimal"

Passed!  - Failed: 0, Passed: 28, Skipped: 0, Total: 28, Duration: 2s
```

| Test Class | Tests | Result |
|---|---|---|
| `ChangeTrackingTests` | 16 | All passed |
| `QueryTranslationTests` | 12 | All passed |

### QueryTranslationTests breakdown

```
✓ WholeEntity_ToQueryString_ContainsPriceColumn
✓ WholeEntity_ToQueryString_ContainsCreatedAtColumn
✓ Projected_ToQueryString_DoesNotContainPriceColumn
✓ Projected_ToQueryString_DoesNotContainCreatedAtColumn
✓ Projected_ToQueryString_ContainsIdNameCategoryColumns
✓ ClientEval_BugQuery_SqlFetchesAllColumns_BeforeToListBreaksTheChain
✓ ClientEval_FixedQuery_SqlDoesNotFetchPriceOrCreatedAt
✓ Projected_ReturnsCorrectDtoValues
✓ Projected_DtoHasNoPrice_NorCreatedAt_Properties
✓ ClientEval_Fixed_ReturnsCorrectResults
✓ Projected_NoMatchingRows_ReturnsEmptyList
```

> SQL shape is asserted via `IQueryable.ToQueryString()` — returns the SQL EF would
> send without executing the query, so tests are fast and deterministic.

---

## Demo Run — `dotnet run`

### Full Terminal Output

```
PS C:\Users\LENOVO\...\piece2_QueryTranslation> cd EFCoreDemo
PS C:\Users\LENOVO\...\piece2_QueryTranslation\EFCoreDemo> dotnet run

[Seeder] Database already seeded — skipping.


═════════════════════════════════════════════════════════════════
  PART 1 — Change Tracking Demo
═════════════════════════════════════════════════════════════════
SCENARIO A: Tracked query → modify → SaveChanges
  [Query]  Id=1, Name='Vintage Widget #00001', Price=1.11
  [State]  EntityState right after query   : Unchanged
  [Modify] Price changed in-memory              : 1.11 → 101.11
  [State]  EntityState BEFORE DetectChanges()   : Unchanged  ← lazy — snapshot not compared yet
  [State]  EntityState AFTER  DetectChanges()   : Modified
  [Track]  Modified properties                   : Price
  [Save]   SaveChanges() rows affected     : 1
  [Verify] Price in DB (new context)       : 101.11
  [Result] UPDATE PERSISTED                : YES ✓

SCENARIO B: AsNoTracking query → modify → SaveChanges
  [Query]  Id=1, Name='Vintage Widget #00001', Price=101.11
  [Track]  Is entity in ChangeTracker      : False  (expected: False)
  [Modify] Price changed in-memory         : 101.11 → 656.11
  [Save]   SaveChanges() rows affected     : 0  (expected: 0)
           ^ nothing was tracked — no SQL generated.
  [Verify] Price in DB (new context)       : 101.11
  [Result] DB UNCHANGED (no stale write)   : YES ✓


═════════════════════════════════════════════════════════════════
  PART 2 — Identity Resolution Demo
═════════════════════════════════════════════════════════════════
  A: Tracked — same key queried twice
  Id targeted             : 4
  first  RuntimeHash      :   10429724
  second RuntimeHash      :   10429724
  ReferenceEquals(a, b)   : True  (expected: True)
  ChangeTracker.Entries   : 1

  B: AsNoTracking — same key queried twice
  Id targeted             : 4
  first  RuntimeHash      :   60815118
  second RuntimeHash      :   10465156
  ReferenceEquals(a, b)   : False  (expected: False)
  ChangeTracker.Entries   : 0  (always 0 — nothing tracked)

  C: AsNoTrackingWithIdentityResolution
  Two SEPARATE queries for same Id:
  ReferenceEquals         : False  (expected: False — different query scopes)


═════════════════════════════════════════════════════════════════
  PART 3 — Performance Benchmark (10,000-row full read)
═════════════════════════════════════════════════════════════════
  ┌──────────────────────────────┬──────────────┬──────────────────┐
  │ Query Type                   │  Time (ms)   │  Allocated (MB)  │
  ├──────────────────────────────┼──────────────┼──────────────────┤
  │ Tracked (default)            │       176 ms │       11.19 MB  │
  │ AsNoTracking                 │        59 ms │        4.81 MB  │
  ├──────────────────────────────┼──────────────┼──────────────────┤
  │ Ratio (tracked / no-track)   │       2.98x │          2.32x  │
  └──────────────────────────────┴──────────────┴──────────────────┘


═════════════════════════════════════════════════════════════════
  PART 5 — Query Translation + Projections   ← THIS PIECE
═════════════════════════════════════════════════════════════════
```

---

## Part 5 Focus — Query Translation + Projections

### Whole-Entity Query — Generated SQL

**C# (original — pulls every column):**
```csharp
var products = await ctx.Products
    .Where(p => p.Category == "Electronics")
    .AsNoTracking()
    .Take(3)
    .ToListAsync();
```

**SQL EF sent to the DB (captured via `LogTo`):**
```sql
SELECT "p"."Id", "p"."Category", "p"."CreatedAt", "p"."Name", "p"."Price"
FROM "Products" AS "p"
WHERE "p"."Category" = 'Electronics'
LIMIT 3
```

```
Columns fetched : Id, Name, Price, Category, CreatedAt  (ALL 5)
Rows returned   : 3

PROBLEM: Price and CreatedAt are never used by the caller, yet
every row transfers those bytes over the network / from disk.
On a 10k-row table, that is wasted I/O on every request.
```

---

### Projected DTO Query — Lean SQL

**DTO (only what callers need):**
```csharp
public sealed class ProductSummaryDto
{
    public int    Id       { get; init; }
    public string Name     { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    // Price and CreatedAt intentionally omitted
}
```

**C# (rewritten with `.Select(dto)` before materialisation):**
```csharp
var dtos = await ctx.Products
    .Where(p => p.Category == "Electronics")
    .AsNoTracking()
    .Select(p => new ProductSummaryDto
    {
        Id       = p.Id,
        Name     = p.Name,
        Category = p.Category
    })
    .Take(3)
    .ToListAsync();
```

**SQL EF sent to the DB (captured via `LogTo`):**
```sql
SELECT "p"."Id", "p"."Name", "p"."Category"
FROM "Products" AS "p"
WHERE "p"."Category" = 'Electronics'
LIMIT 3
```

```
Columns fetched : Id, Name, Category  (3 of 5 — Price + CreatedAt GONE)
Rows returned   : 3

GAIN: EF translated the .Select() to a SQL projection.
Price and CreatedAt are not referenced — they never leave the DB engine.
Fewer bytes per row = less I/O, less memory allocation, faster response.
```

---

### Client-Side Evaluation — Bug Caught

**The bug — `.ToList()` called BEFORE `.Select(dto)`:**
```csharp
// BAD — accidental client-side evaluation
var result = ctx.Products
    .Where(p => p.Category == "Electronics")
    .AsNoTracking()
    .ToList()                                   // ← DB call fires HERE
    .Select(p => new ProductSummaryDto          // ← C# LINQ, NOT SQL
    {
        Id       = p.Id,
        Name     = p.Name,
        Category = p.Category
    })
    .ToList();
```

**SQL actually sent (logged by EF from the early `.ToList()`):**
```sql
SELECT "p"."Id", "p"."Category", "p"."CreatedAt", "p"."Name", "p"."Price"
FROM "Products" AS "p"
WHERE "p"."Category" = 'Electronics'
```

```
ALL columns fetched. Price and CreatedAt travel from DB to app
even though .Select(dto) discards them immediately after.
No exception is raised — this silently wastes I/O on every call.
```

---

### Fixed Query — Lean SQL Confirmed

**The fix — `.Select(dto)` moved BEFORE `.ToListAsync()`:**
```csharp
// GOOD — .Select() stays on IQueryable, EF translates it to SQL
var result = await ctx.Products
    .Where(p => p.Category == "Electronics")
    .AsNoTracking()
    .Select(p => new ProductSummaryDto
    {
        Id       = p.Id,
        Name     = p.Name,
        Category = p.Category
    })
    .ToListAsync();   // ← SQL projection fires here
```

**SQL after the fix:**
```sql
SELECT "p"."Id", "p"."Name", "p"."Category"
FROM "Products" AS "p"
WHERE "p"."Category" = 'Electronics'
```

```
Price and CreatedAt are GONE from the SQL.
The DB engine does the projection — only three columns travel.
```

---

### Why Projection Is Better

| | Whole entity | DTO projection |
|---|---|---|
| Columns in SQL | 5 | 3 |
| `Price` transferred per row | Yes | No |
| `CreatedAt` transferred per row | Yes | No |
| Change tracker entries | 1 per row (if tracked) | 0 — DTO is not an entity |
| Memory snapshot overhead | Yes (EF stores original values) | None |
| Risk of accidental `SaveChanges` | Yes | None — DTOs cannot be saved |

**Root cause of the client-eval bug:**

LINQ's `.Select()` behaves differently depending on the type it runs on:
- On `IQueryable<T>` (EF query) → EF translates it to a SQL `SELECT` clause — **server-side**.
- On `IEnumerable<T>` (C# collection) → it runs as a C# loop **after** data is already fetched.

Calling `.ToList()` anywhere in the chain **downgrades** `IQueryable → IEnumerable`.
Every operator after it (`Select`, `Where`, `OrderBy`) runs in C#, not in SQL.

> **Rule:** Keep the chain on `IQueryable` until the final `.ToListAsync()` call.

---

### SQL Comparison Summary

| Query | SQL generated | Columns | Notes |
|---|---|---|---|
| Whole entity | `SELECT Id, Category, CreatedAt, Name, Price` | 5 | Over-fetches |
| DTO projection | `SELECT Id, Name, Category` | 3 | Lean — DB projects |
| Bug (early `.ToList()`) | `SELECT Id, Category, CreatedAt, Name, Price` | 5 | Silent waste, no exception |
| Fix (`.Select()` first) | `SELECT Id, Name, Category` | 3 | Same as projection |
