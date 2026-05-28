# Day 10 — EF Core Change Tracker + AsNoTracking

## Project Structure

```
Day10/EFcore/
├── EFCoreDemo/
│   ├── EFCoreDemo.csproj
│   ├── Program.cs
│   ├── Models/Product.cs
│   ├── Data/AppDbContext.cs
│   ├── Seeder/DataSeeder.cs
│   └── Demos/
│       ├── ChangeTrackingDemo.cs
│       ├── IdentityResolutionDemo.cs
│       ├── PerformanceBenchmark.cs
│       └── EdgeCasesDemo.cs
├── EFCoreDemo.Tests/
│   ├── EFCoreDemo.Tests.csproj
│   └── ChangeTrackingTests.cs
├── Reflection.md
└── README.md
```

---

## How to Run

**Run the demo app:**
```
dotnet run --project EFCoreDemo
```

**Run all tests (summary):**
```
dotnet test EFCoreDemo.Tests
```

**Run all tests with each test name shown:**
```
dotnet test EFCoreDemo.Tests --logger "console;verbosity=detailed"
```

**Run a single test by name:**
```
dotnet test EFCoreDemo.Tests --filter "AsNoTracking_PriceChange_DoesNotPersistToDB_ConfirmedBySecondContext"
```

**Run a group by partial name:**
```
dotnet test EFCoreDemo.Tests --filter "AsNoTracking"
```

---

## Demo Output

```
╔══════════════════════════════════════════════════════════════════╗
║     EF Core Change Tracker + AsNoTracking — Complete Demo        ║
║     ThinkSchool Day10 · Piece 1                                  ║
╚══════════════════════════════════════════════════════════════════╝

[Seeder] Database already seeded — skipping.


═════════════════════════════════════════════════════════════════
  PART 2 — Change Tracking Demo
═════════════════════════════════════════════════════════════════
SCENARIO A: Tracked query → modify → SaveChanges
  [Query]  Id=1, Name='Vintage Widget #00001', Price=1.11
  [State]  EntityState right after query   : Unchanged
  [Modify] Price changed in-memory         : 1.11 → 101.11
  [State]  EntityState after modification  : Unchanged
  [Track]  Modified properties tracked     :
  [Save]   SaveChanges() rows affected     : 1
  [Verify] Price in DB (new context)       : 101.11
  [Result] UPDATE PERSISTED                : YES ✓

SCENARIO B: AsNoTracking query → modify → SaveChanges
  [Query]  Id=1, Name='Vintage Widget #00001', Price=101.11
  [Track]  Is entity in ChangeTracker      : False  (expected: False)
  [Modify] Price changed in-memory         : 101.11 → 656.11
  [Save]   SaveChanges() rows affected     : 0  (expected: 0)
           ^ EF Core has no tracked entity to diff — it generates zero SQL.
  [Verify] Price in DB (new context)       : 101.11
  [Result] DB UNCHANGED (no stale write)   : YES ✓

═════════════════════════════════════════════════════════════════
  PART 3 — Identity Resolution Demo
═════════════════════════════════════════════════════════════════
  A: Tracked — same key queried twice
  Id targeted             : 4
  first  RuntimeHash      :   10429724
  second RuntimeHash      :   10429724
  ReferenceEquals(a, b)   : True  (expected: True)
  ChangeTracker.Entries   : 1  (only 1 — second query was served from identity map, no DB round-trip)

  WHY: EF Core's identity map (a Dictionary<IKey,object> inside the
       ChangeTracker) returns the cached instance when the same key
       is seen again. The second SQL query IS still sent to the DB,
       but the materializer looks up the key and returns the existing
       C# object instead of allocating a new one.

  B: AsNoTracking — same key queried twice
  Id targeted             : 4
  first  RuntimeHash      :   60815118
  second RuntimeHash      :   10465156
  ReferenceEquals(a, b)   : False  (expected: False)
  ChangeTracker.Entries   : 0  (always 0 — nothing tracked)

  WHY: Without an identity map, EF materializes a fresh heap object
       on every query. Two queries for the same row = two distinct
       C# objects with identical data but different addresses.

  C: AsNoTrackingWithIdentityResolution
  Two SEPARATE queries for same Id:
  first  RuntimeHash      :   54749715
  second RuntimeHash      :   18887690
  ReferenceEquals         : False  (expected: False — different query scopes)

  Single flat batch (10 rows):
  AsNoTracking           loaded: 10 distinct objects
  ATNWIR                 loaded: 10 distinct objects

  For flat queries both look identical. The REAL advantage appears
  in a JOIN with Include():  Orders.AsNoTrackingWithIdentityResolution()
                               .Include(o => o.Product).ToList()
  Without ATNWIR: 50 orders sharing 1 product → 50 Product objects.
  With    ATNWIR: 50 orders sharing 1 product → 1 Product object.

  WHY AsNoTrackingWithIdentityResolution EXISTS

  Problem: A 1:N Include() query with AsNoTracking allocates a
           fresh parent entity per child row. If Product 42 has 100
           Reviews, you get 100 duplicate Product instances — all
           identical, all wasting heap space.

  Solution: ATNWIR builds a temporary identity map that lives for
            the duration of ONE query. It de-duplicates parent refs
            within that result set WITHOUT adding them to the
            ChangeTracker. Objects remain untracked — SaveChanges()
            still ignores them.

  Memory tradeoff:
    + Prevents N duplicate parent objects in a high fan-out JOIN.
    - Allocates a Dictionary<IKey,object> for every query call.
    → Use ATNWIR only with Include() / navigation-heavy queries.
    → Use plain AsNoTracking() for flat single-table reads.
    → Use default tracking whenever you plan to modify + save.

═════════════════════════════════════════════════════════════════
  PART 4 — Performance Benchmark (10,000-row full read)
═════════════════════════════════════════════════════════════════
  Methodology:
    • 2 warm-up runs discarded (heats JIT + SQLite page cache)
    • 5 measured runs per query type
    • Stopwatch for wall time; GC.GetAllocatedBytesForCurrentThread() for heap bytes
    • Fresh DbContext per run (tracked context carries snapshot per entity)
    • Synchronous queries so all allocation is on the same thread
    • Median of 5 runs reported to suppress outliers

  Warming up .. done.

  ┌──────────────────────────────┬──────────────┬──────────────────┐
  │ Query Type                   │  Time (ms)   │  Allocated (MB)  │
  ├──────────────────────────────┼──────────────┼──────────────────┤
  │ Tracked (default)            │       141 ms │       11.19 MB   │
  │ AsNoTracking                 │        62 ms │        4.81 MB   │
  ├──────────────────────────────┼──────────────┼──────────────────┤
  │ Ratio (tracked / no-track)   │       2.27x  │          2.32x   │
  └──────────────────────────────┴──────────────┴──────────────────┘

  Raw runs — Tracked    :  533ms/11.2MB   249ms/11.2MB   126ms/11.2MB   141ms/11.2MB    81ms/11.2MB
  Raw runs — AsNoTracking:  198ms/4.8MB    59ms/4.8MB    72ms/4.8MB    62ms/4.8MB    44ms/4.8MB

  WHAT THE NUMBERS MEAN:
    Memory delta  The ChangeTracker stores an original-value snapshot
                  (object[] of boxed primitives) for every tracked entity.
                  For 10k Product rows this adds ~200–400 bytes per entity
                  on top of the entity objects themselves.

    Time delta    Tracking overhead comes from:
                    1. Dictionary<IKey,object> lookup per materialized row
                    2. Snapshot boxing (Price, CreatedAt → heap-boxed object[])
                    3. EntityEntry wrapper allocation per entity
                    4. DetectChanges() scan on SaveChanges()

    Caveat        I/O cost (SQLite read from OS page cache) is IDENTICAL
                  for both queries. The measured delta is pure EF Core
                  materialization + tracking bookkeeping overhead.
                  On a remote SQL Server the I/O cost would dominate and
                  the relative difference would look smaller — but the
                  absolute memory overhead is the same regardless of server.

═════════════════════════════════════════════════════════════════
  PART 5 — Edge Cases & Production Failure Modes
═════════════════════════════════════════════════════════════════
EC-1  AsNoTracking update is a SILENT no-op — no exception
  product.Price in memory : 10100.11  (object was mutated)
  SaveChanges() rows      : 0  ← EF Core generated ZERO SQL
  Original price in DB    : 101.11  (unchanged)

  PRODUCTION IMPACT:
    • No exception is thrown. No warning is logged.
    • The caller receives a 200 OK response with stale data in the DB.
    • The bug surfaces only when users report that changes aren't saved.
    • Hardest to reproduce: the error is in a repository method that
      conditionally applies AsNoTracking — the caller has no way to know.
  FIX: Never use AsNoTracking() in code paths that modify + save.

EC-2  Tracked read of all 10k rows inflates memory
  Entities loaded          : 10,000
  ChangeTracker.Entries    : 10,000  (one entry per entity)
  GC.GetTotalMemory delta  : ~8,076 KB

  Each tracked entry holds:
    • An EntityEntry wrapper object
    • An InternalEntityEntry with original-value snapshot (object[])
    • Boxed copies of every value-type property (int, decimal, DateTime)

  PRODUCTION IMPACT:
    A background reporting job that does context.Products.ToList()
    for a 100k-row table will hold ~40-80 MB of tracker data that
    is useless if no update follows. On a server with 512 MB RAM
    and 20 concurrent workers, this exhausts memory.
  FIX: Use AsNoTracking() for all read-only operations (reports, APIs).

EC-3  Long-lived DbContext — tracker grows with every query
  Loading 200 products per 'request' into the same DbContext:
    After request 1: ChangeTracker.Entries =   200  (+200 per call)
    After request 2: ChangeTracker.Entries =   400  (+200 per call)
    After request 3: ChangeTracker.Entries =   600  (+200 per call)
    After request 4: ChangeTracker.Entries =   800  (+200 per call)
    After request 5: ChangeTracker.Entries =  1000  (+200 per call)

  PRODUCTION IMPACT:
    In ASP.NET Core with a SINGLETON DbContext (a common misuse),
    every API request loads more rows into the same context.
    The tracker never shrinks until the process restarts.
    DetectChanges() on SaveChanges() scans ALL tracked entries —
    the more entries, the slower every future SaveChanges() call.
  FIX 1: Always register DbContext as SCOPED (one per HTTP request).
  FIX 2: Call context.ChangeTracker.Clear() between logical units.
  FIX 3: Use AsNoTracking() so entries never accumulate.

EC-4  AsNoTrackingWithIdentityResolution: overhead for flat queries vs. benefit for Include(1:N) queries

  Query scenario A — FLAT (single table, no navigation):
    context.Products.AsNoTracking().ToList()      → best choice
    context.Products.AsNoTrackingWithIdentityResolution().ToList()
      → same result, but allocates an extra Dictionary<IKey,object>
        that serves no purpose because no entity appears twice.
        PURE OVERHEAD.

  Query scenario B — JOIN (Include with 1:N navigation):
    context.Orders.AsNoTracking().Include(o => o.Product).ToList()
      → Product with Id=5 has 50 orders → 50 Product objects in RAM.
    context.Orders.AsNoTrackingWithIdentityResolution()
                  .Include(o => o.Product).ToList()
      → Product with Id=5 has 50 orders → 1 Product object in RAM.
        Memory saving: 49 × sizeof(Product).

  Rule: Only pay the ATNWIR dictionary cost when fan-out is high enough
        to recover it through de-duplication. Measure before committing.

EC-5  Accidentally mixing tracked and untracked entities
  tracked   Id=1       InChangeTracker: True
  untracked Id=10000   InChangeTracker: False
  After modifying both and SaveChanges(): rows affected = 1
  tracked.Price saved    : 1.11  (expected: 1.11)
  untracked.Price in DB  : 783.80  (unchanged, expected)

  PRODUCTION IMPACT:
    A service receives a Product from a repository. The repository
    returns AsNoTracking on GETs and tracked on POSTs. The caller
    code path treats them identically — compiles, runs, no crash,
    but 50% of updates silently fail depending on which code path ran.
  FIX: Repository interfaces should document tracking behavior. Consider
       separate method names: FindForRead() vs FindForUpdate().

EC-6  Adding an entity with a key already tracked throws
  Product Id=4 is tracked: True
  Attempting ctx.Products.Add(new Product { Id=4 }) ...
  EXCEPTION (expected): The instance of entity type 'Product' cannot be tracked
  because another instance with the same key value for {'Id'} is already being tracked...

  PRODUCTION IMPACT:
    Arises in PUT/PATCH handlers that: (1) load an entity for validation,
    (2) map a DTO to a new entity object, (3) call Add() to 'save' it.
    The entity from step 1 is still tracked — step 3 collides.
  FIX: Use ctx.Entry(mapped).State = EntityState.Modified for detached
       updates, OR call ctx.ChangeTracker.Clear() before re-attaching.

╔══════════════════════════════════════════════════════════════════╗
║  All demos complete.                                              ║
║  Run tests:  dotnet test ../EFCoreDemo.Tests                      ║
║  Reflection: see Reflection.md at repo root                      ║
╚══════════════════════════════════════════════════════════════════╝
```

---

## Test Output

```
dotnet test EFCoreDemo.Tests --logger "console;verbosity=detailed"
```

```
  Passed EFCoreDemo.Tests.ChangeTrackingTests.AsNoTracking_SameId_QueriedTwice_ReturnsDifferentReferences [2 s]
  Passed EFCoreDemo.Tests.ChangeTrackingTests.AsNoTracking_EntityIsNotInChangeTracker [90 ms]
  Passed EFCoreDemo.Tests.ChangeTrackingTests.ChangeTracker_Clear_DetachesAllTrackedEntities [32 ms]
  Passed EFCoreDemo.Tests.ChangeTrackingTests.Tracked_EntityState_BecomesModified_AfterPropertyChange [13 ms]
  Passed EFCoreDemo.Tests.ChangeTrackingTests.UsingEntryStateModified_IsCorrectWayToUpdateDetachedEntity [55 ms]
  Passed EFCoreDemo.Tests.ChangeTrackingTests.Tracked_SaveChanges_ReturnsOneRowAffected_OnSingleModification [5 ms]
  Passed EFCoreDemo.Tests.ChangeTrackingTests.AsNoTracking_PriceChange_DoesNotPersistToDB_ConfirmedBySecondContext [7 ms]
  Passed EFCoreDemo.Tests.ChangeTrackingTests.Tracked_PriceChange_PersistedToDB_ConfirmedBySecondContext [6 ms]
  Passed EFCoreDemo.Tests.ChangeTrackingTests.AddingDuplicateKey_WhenAlreadyTracked_ThrowsInvalidOperationException [15 ms]
  Passed EFCoreDemo.Tests.ChangeTrackingTests.Tracked_NameChange_PersistedToDB_ConfirmedBySecondContext [12 ms]
  Passed EFCoreDemo.Tests.ChangeTrackingTests.ChangeTracker_Clear_ThenModify_ChangesAreNotSaved [8 ms]
  Passed EFCoreDemo.Tests.ChangeTrackingTests.AsNoTracking_SaveChanges_ReturnsZeroRows [9 ms]
  Passed EFCoreDemo.Tests.ChangeTrackingTests.AsNoTrackingWithIdentityResolution_SeparateQueries_ReturnDifferentReferences [21 ms]
  Passed EFCoreDemo.Tests.ChangeTrackingTests.Tracked_SameId_QueriedTwice_ReturnsSameReference [12 ms]
  Passed EFCoreDemo.Tests.ChangeTrackingTests.Tracked_EntityState_IsUnchanged_RightAfterQuery [6 ms]
  Passed EFCoreDemo.Tests.ChangeTrackingTests.AsNoTracking_MultipleModifications_NonePersistedToDB [11 ms]

Test Run Successful.
Total tests: 16
     Passed: 16
  Total time: 5.1133 Seconds
```

### Test Groups

| Group | Tests | What they prove |
|---|---|---|
| Tracked state transitions | 2 | `Unchanged` after query, `Modified` after property change |
| Tracked persistence | 3 | Price/Name changes reach the DB (verified via second context) |
| AsNoTracking non-persistence | 4 | Zero rows saved, entity not in tracker, DB unchanged |
| Identity resolution | 3 | `ReferenceEquals` behavior for tracked, AsNoTracking, ATNWIR |
| ChangeTracker.Clear() | 2 | Clear() detaches all; post-clear saves produce 0 rows |
| Edge cases | 2 | Duplicate-key exception; `EntityState.Modified` correct update pattern |
