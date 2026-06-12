# Day 21 — HybridCache + Stampede Protection

> All metrics below are **live measured values** from a real test run on this machine (2026-06-12).  
> Infrastructure: SQL Server + Redis in Docker, API on localhost:5000.

---

## 1. Implementation Overview

| Component | File | Purpose |
|---|---|---|
| Cache key helpers | `Cache/CacheKeys.cs` | Stable key strings + tag constants |
| Hit/miss counters | `Cache/CacheMetrics.cs` | Thread-safe `Interlocked` counters |
| HybridCache registration | `Extensions/InfrastructureExtensions.cs` | L1 + L2 Redis + stampede lock |
| Hot read (by ID) | `Endpoints/QuoteEndpoints.cs` | `GET /api/quotes/{id}` |
| Hot read (list) | `Endpoints/QuoteEndpoints.cs` | `GET /api/quotes` |
| Cache invalidation | `Endpoints/QuoteEndpoints.cs` | POST + DELETE evict tags |
| Metrics endpoint | `Endpoints/QuoteEndpoints.cs` | `GET /api/quotes/cache-stats` |
| Stampede demo | `Endpoints/QuoteEndpoints.cs` | `GET /api/quotes/stampede-demo` |
| No-cache baseline | `Endpoints/QuoteEndpoints.cs` | `GET /api/quotes/no-cache/{id}` |
| Load test | `k6-hybrid-cache.js` | 3 scenarios, 50 VUs |

---

## 2. Cache Wiring

### Registration — `InfrastructureExtensions.cs`

```csharp
var redisConn = configuration.GetConnectionString("Redis");
if (!string.IsNullOrEmpty(redisConn))
{
    services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConn;   // "localhost:6379" in Development
        options.InstanceName = "QuotesApi:";
    });
}

services.AddHybridCache(options =>
{
    options.DefaultEntryOptions = new HybridCacheEntryOptions
    {
        Expiration = TimeSpan.FromMinutes(5),           // L2 Redis TTL
        LocalCacheExpiration = TimeSpan.FromSeconds(30) // L1 in-memory TTL
    };
});

services.AddSingleton<CacheMetrics>();
```

### Cache keys — `Cache/CacheKeys.cs`

```csharp
public static string QuoteById(int id) => $"q:id:{id}";
public static string QuotesList(int page, int size, string? author, string? text)
    => $"q:list:{page}:{size}:{author ?? ""}:{text ?? ""}";
public const string TagLists = "q:lists";
public const string TagIds   = "q:ids";
```

### Hot read — `GET /api/quotes/{id}`

```csharp
metrics.RecordRequest();
var quote = await hybridCache.GetOrCreateAsync(
    CacheKeys.QuoteById(id),
    async cancel =>
    {
        metrics.RecordDbQuery();   // fires only on cache MISS
        return await handler.HandleAsync(new GetQuoteByIdQuery(id), cancel);
    },
    new HybridCacheEntryOptions { Expiration = TimeSpan.FromMinutes(5) },
    [CacheKeys.TagIds],
    cancellationToken);
```

### Cache invalidation

```csharp
// POST /api/quotes
await hybridCache.RemoveByTagAsync(CacheKeys.TagLists, cancellationToken);

// DELETE /api/quotes/{id}
await hybridCache.RemoveAsync(CacheKeys.QuoteById(id), cancellationToken);
await hybridCache.RemoveByTagAsync(CacheKeys.TagLists, cancellationToken);
```

---

## 3. Cache Miss → Hit Transition (measured)

**Method:** Reset stats → hit same ID 3 times → read `/api/quotes/cache-stats`

| Request | requests | dbQueries | hits | hitRatePct |
|---|---|---|---|---|
| 1st (cold miss) | 1 | **1** | 0 | 0% |
| 3rd (2 warm hits) | 3 | **1** | 2 | **66.7%** |

`dbQueries` stayed at 1 for all three calls. Requests 2 and 3 were served from L1 in-process memory — zero DB round-trips.

### Screenshots

![Cache Wiring](screenshots/cache_wiring.png)

![After Cold Miss](screenshots/after_cold_miss.png)

![After Warm Hit](screenshots/after_warm_hit.png)

---

## 4. Load Test — Before vs After (k6, live run)

**Setup:** 50 VUs × 20 s × 200 seeded quote IDs (IDs 1–200)  
**Run date:** 2026-06-12 | **Total iterations:** 58,512 | **Duration:** 52.6 s

### Scenario A — No-cache baseline (`/no-cache/{id}`)

Every request calls the query handler directly. No caching.

| Metric | Value |
|---|---|
| DB queries total | **24,367** |
| DB queries/sec | **463 /sec** |
| avg latency | 40.7 ms |
| median latency | 38.1 ms |
| p(90) latency | 52.0 ms |
| p(95) latency | 57.2 ms |
| max latency | 492 ms |

### Scenario B — HybridCache (`/{id}`)

First hit per unique ID → DB. All subsequent hits → L1 in-process memory.

| Metric | Value |
|---|---|
| Total requests served | **34,144** |
| DB queries (cache misses) | **200** (1 per unique ID) |
| DB queries/sec after warm-up | **0 /sec** |
| Cache hits | **33,944** |
| Hit rate | **99.4%** |
| DB load drop | **99.4%** |
| avg latency | 29.1 ms |
| median latency | 27.9 ms |
| p(90) latency | 37.9 ms |
| p(95) latency | 43.0 ms |
| max latency | 115 ms |

### Summary

| Metric | Before (no cache) | After (HybridCache) | Change |
|---|---|---|---|
| DB queries/sec | 463 | ~0 (post warm-up) | **−100%** |
| DB queries total (20 s) | 24,367 | 200 | **−99.2%** |
| Hit rate | 0% | 99.4% | **+99.4 pp** |
| avg latency | 40.7 ms | 29.1 ms | −28% |
| p(90) latency | 52.0 ms | 37.9 ms | −27% |
| p(95) latency | 57.2 ms | 43.0 ms | −25% |

![k6 Load Test](screenshots/load_test.png)

---

## 5. Stampede Protection (measured)

**Endpoint:** `GET /api/quotes/stampede-demo?concurrency=N`  
**Mechanism:** N concurrent goroutines hit the same cold key with a 200 ms factory delay.  
Counts actual factory invocations for both `IMemoryCache` and `HybridCache`.

### Results

| Concurrency | IMemoryCache factory calls | HybridCache factory calls | DB queries saved |
|---|---|---|---|
| 20 | **20** | **1** | **19** |
| 50 | **50** | **1** | **49** |

### concurrency = 20 (actual response)

```json
{
  "concurrency": 20,
  "factoryDelayMs": 200,
  "memoryCache": {
    "factoryCalls": 20,
    "stampedeOccurred": true,
    "wastedDbQueries": 19
  },
  "hybridCache": {
    "factoryCalls": 1,
    "stampedeEliminated": true,
    "savedDbQueries": 19
  },
  "verdict": "20 concurrent requests → 1 DB call. HybridCache coalesced 19 waiters. IMemoryCache fired 20 DB calls for the same load."
}
```

### concurrency = 50 (actual response)

```json
{
  "concurrency": 50,
  "factoryDelayMs": 200,
  "memoryCache": {
    "factoryCalls": 50,
    "stampedeOccurred": true,
    "wastedDbQueries": 49
  },
  "hybridCache": {
    "factoryCalls": 1,
    "stampedeEliminated": true,
    "savedDbQueries": 49
  },
  "verdict": "50 concurrent requests → 1 DB call. HybridCache coalesced 49 waiters. IMemoryCache fired 50 DB calls for the same load."
}
```

### Why IMemoryCache causes a stampede

`IMemoryCache.GetOrCreateAsync` has no coalescing lock. All N threads call `TryGetValue` before the first factory completes (factory takes 200 ms), all get `false`, and all enter the factory independently → N DB queries for a single key.

`HybridCache.GetOrCreateAsync` maintains a per-key in-flight slot. The first caller runs the factory; all other concurrent arrivals suspend on that slot and receive the same result → **exactly 1 DB query regardless of N**.

![Stampede Protection](screenshots/stampade_protection.png)

---

## 6. k6 Full Output (actual)

```
scenarios: 3 scenarios, 101 max VUs, 52.6s total

THRESHOLDS
  ✓ checks  rate=100.00%   (58,515 / 58,515 passed)

CHECKS
  ✓ [baseline] status 200
  ✓ [cached]   status 200
  ✓ [stampede] status 200
  ✓ [stampede] HybridCache fires exactly 1 DB call
  ✓ [stampede] IMemoryCache fires >1 DB calls
  ✓ [stampede] stampede eliminated flag is true

STAMPEDE DEMO
  IMemoryCache factory calls : 20  ← thundering herd
  HybridCache  factory calls : 1   ← stampede eliminated
  DB queries saved           : 19

CACHE STATS (HybridCache scenario)
  Total requests : 34,144
  DB queries     : 200   ← 1 per unique ID, then 0 forever
  Cache hits     : 33,944
  Hit rate       : 99.4%
  DB load drop   : 99.4%

CUSTOM METRICS
  db_queries_no_cache : 24,367 total  |  463/sec
  latency_no_cache    : avg=40.73ms   med=38.07ms  p(90)=52.03ms  p(95)=57.24ms  max=492ms
  latency_cached      : avg=29.14ms   med=27.89ms  p(90)=37.90ms  p(95)=43.01ms  max=115ms

HTTP
  http_req_duration   : avg=33.97ms  p(90)=46.62ms  p(95)=52.40ms
  http_req_failed     : 0.00%  (0 of 58,515)
  http_reqs           : 58,515  |  1,111/sec

EXECUTION
  iterations  : 58,512
  vus_max     : 101
  duration    : 52.6 s
```

---

## 7. Limitations and Notes

| Item | Detail |
|---|---|
| stampede-demo max concurrency | Clamped at 50 via `Math.Clamp(concurrency, 2, 50)` — `?concurrency=100` runs as 50 |
| Null caching | Non-existent IDs are cached for the full TTL; mitigate with a short TTL for nulls in production |
| L1 TTL vs L2 TTL | L1=30 s, L2=5 min — L1-evicted entries are refetched from Redis (not DB) until the 5-min TTL expires |
| Redis is optional | Empty `ConnectionStrings:Redis` runs L1-only; integration tests work without Redis |

---

## 8. How to Reproduce

```powershell
# Start infrastructure
docker compose up -d sqlserver redis

# Start API (separate terminal)
cd backend
dotnet run

# Verify cache wiring
curl.exe -X DELETE http://localhost:5000/api/quotes/cache-stats
curl.exe http://localhost:5000/api/quotes/1
curl.exe http://localhost:5000/api/quotes/cache-stats   # requests=1, dbQueries=1, hits=0
curl.exe http://localhost:5000/api/quotes/1
curl.exe http://localhost:5000/api/quotes/cache-stats   # requests=2, dbQueries=1, hits=1

# Stampede protection
curl.exe "http://localhost:5000/api/quotes/stampede-demo?concurrency=20"
curl.exe "http://localhost:5000/api/quotes/stampede-demo?concurrency=50"

# Full load test
k6 run k6-hybrid-cache.js
```
