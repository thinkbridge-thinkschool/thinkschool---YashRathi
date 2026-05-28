# Day 10 — EF Core Change Tracker + AsNoTracking  
## Mentor Reflection

---

### What I Learned

The most important thing I learned is that **the ChangeTracker is the mechanism, not a side effect**.  
EF Core does not "watch" your objects for mutations. It takes a snapshot of original values when an entity is loaded
and then, when `SaveChanges()` is called, runs `DetectChanges()` which compares current values against
that snapshot. Any property that differs becomes a column in the `UPDATE` SQL. If there is no snapshot
(because you used `AsNoTracking`), there is no comparison, and no SQL is generated — silently.

The second thing I learned is how the **identity map** works within a tracked context. When you load
a `Product` with `Id = 5` and then query `Id = 5` again in the same context, EF Core does issue a SQL
query, but when it reads the result row and finds the key already in its identity dictionary, it returns
the existing C# object rather than allocating a new one. This means the object you hold is the *exact
same reference* as the one currently in the tracker — any mutation you make to it is immediately visible
to the tracker on the next `SaveChanges()`.

---

### Where I Would NOT Use AsNoTracking

**I would NOT use `AsNoTracking` when I plan to modify and save entities because EF Core will not
detect changes automatically.**

Specifically, I would never use it in:
- `PUT` / `PATCH` API endpoints that load an entity and update fields
- Domain service methods where an entity is mutated as part of business logic
- Any code that calls `entity.Property = newValue; context.SaveChanges()`
- Repository methods that are called from a "write" code path

The failure is completely silent: zero exceptions, zero warnings, zero SQL generated.  
The only safety net is an integration test that verifies the DB state in a fresh context after `SaveChanges()`.

---

### What Surprised Me

**1. The second tracked query is still sent to the database.**  
I assumed that once an entity is tracked, a second `FindAsync(id)` would be fully served from cache.
It is not — EF Core sends the SQL again. The identity map only affects object materialisation:
the row is read from the DB, but instead of allocating a new object, EF hands back the existing reference.
This means stale data from the DB is *ignored* while the entity is tracked — which can be a gotcha
if another process updates the row between your two queries.

**2. `AsNoTrackingWithIdentityResolution` is scoped to a single query, not to the context lifetime.**  
I thought it was a middle-ground mode: untracked but sharing references across the context session.
It is not. Its identity map lives only for the duration of one `ToList()` call (or equivalent). Once that
call returns, the map is discarded. For two separate queries, it behaves identically to `AsNoTracking`.
Its value only appears with high-fanout `Include()` joins, where the same parent appears in dozens of rows.

**3. Tracked query memory overhead is measurable at 10k rows.**  
I expected a minor difference. In practice, EF Core boxes every value-type property (`Price` decimal,
`CreatedAt` DateTime) into an `object[]` snapshot per entity. For 10,000 products this adds up to
several megabytes of extra allocation purely for the purpose of change detection — which is free overhead
if you never modify those entities.

---

### Tradeoffs

| Situation | Choice | Reason |
|---|---|---|
| API `GET` — return a list for serialisation | `AsNoTracking()` | No changes needed; saves memory and time |
| API `PUT` / `PATCH` — load, modify, save | **Default tracking** | ChangeTracker must own the diff |
| Report query / dashboard aggregation | `AsNoTracking()` | Pure read; no point tracking thousands of rows |
| `Include()` with deep 1:N navigation, read-only | `AsNoTrackingWithIdentityResolution()` | Prevents N duplicate parent objects |
| DDD aggregate root | **Default tracking** | EF must manage the full lifecycle including cascade |
| Background batch job (read thousands, no write) | `AsNoTracking()` | Prevents tracker bloat; critical for jobs that run over hours |
| Concurrency token update | **Default tracking** | EF reads the original `RowVersion` from the snapshot |

---

### When Tracking Is Necessary

1. **Any update operation** — even trivial ones like `user.LastLoginAt = DateTime.UtcNow`.
2. **Cascade operations** — EF Core uses tracked dependent entities to generate `DELETE` chains.
3. **Optimistic concurrency** — EF reads the original `RowVersion` / `xmin` from the snapshot to build
   the `WHERE rowversion = @original` clause. Without it, the concurrency check cannot fire.
4. **Graph persistence** — When re-attaching a disconnected object graph (e.g., from a deserialized DTO),
   the tracker needs to determine which entities are new (`Added`) vs. existing (`Modified`).

---

### Limitations I Acknowledge Honestly

- **Benchmark is SQLite-only.** SQLite has no network round-trip cost. On a remote SQL Server, I/O
  will dominate and the *relative* tracked-vs-untracked time ratio will look smaller. The memory
  overhead is identical regardless of database server.

- **`GC.GetAllocatedBytesForCurrentThread()` measures allocations, not live memory.** The counter
  only ever increases (it counts bytes put on the heap, not bytes currently alive). GC may collect
  objects during the measured window and the number won't decrease. The values are directionally
  accurate but should not be treated as a precise live-memory profiler.

- **`GC.Collect()` between runs affects benchmark realism.** I force a full GC before each measurement
  to reduce noise. In production, GC does not fire on command. The real-world overhead of tracked
  queries could be worse than measured here because accumulated tracker data causes more frequent GC.

- **AsNoTracking does not disable lazy loading.** If your entities use lazy-loading proxies and you
  navigate to a collection (`product.Reviews`) on an untracked entity, EF Core will still issue a SQL
  query for the collection. The entity itself is not tracked, but the navigation property load still
  fires. This is a separate concern from what this demo covers.
