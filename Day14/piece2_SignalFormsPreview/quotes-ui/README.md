# Quotes Explorer — Angular 21 UI

Standalone, zoneless Angular 21 frontend for the Week-1 Quotes API. Demonstrates signal-based state, the new control-flow syntax, and `inject()` over constructor injection — all without NgModules or Zone.js.

## Stack

| What | How |
|---|---|
| Change detection | `provideZonelessChangeDetection()` — no Zone.js, CD runs only when signals change |
| State | `signal()` / `computed()` / `effect()` — no RxJS in the component |
| HTTP | `HttpClient` via `inject(HttpClient)` inside the service |
| Template | `@if` / `@for` / `@switch` new control-flow (no `*ngIf` / `*ngFor`) |
| Modules | Zero NgModules — `standalone: true` on `AppComponent` |

## Running locally

Two terminals are required — the Angular dev server proxies `/api/*` to the .NET API.

**Terminal 1 — .NET API (port 5000):**
```powershell
cd c:\Users\LENOVO\OneDrive\Desktop\Thinkschool\Day13\piece1
dotnet run --launch-profile http
```

**Terminal 2 — Angular dev server (port 4200):**
```powershell
cd quotes-ui
npm start
```

Open `http://localhost:4200`. The proxy in `angular.json` routes all `/api` requests to `http://localhost:5000` — no CORS config needed.

## Key files

| File | Purpose |
|---|---|
| `src/app/app.config.ts` | Bootstraps `provideZonelessChangeDetection()` + `provideHttpClient()` |
| `src/app/app.component.ts` | All signal state + `effect()` for auto-fetch on filter/page change |
| `src/app/app.component.html` | `@switch` view-state machine, `@for` quote grid with `@empty`, `@if` filter chips |
| `src/app/quotes.service.ts` | `inject(HttpClient)` — no constructor, `Observable<Quote[]>` |
| `src/app/quote.model.ts` | `Quote` interface (`id`, `author`, `text`, `createdAt`) |
| `proxy.conf.json` | Proxies `/api` → `http://localhost:5000` during `ng serve` |

## Signals in use

```ts
// Writable signals — source of truth
currentPage  = signal(1);
pageSize     = signal(10);
authorSearch = signal('');
quoteSearch  = signal('');
quotes       = signal<Quote[]>([]);
isLoading    = signal(false);
errorMessage = signal<string | null>(null);
hasMore      = signal(true);   // false when last fetch returned < pageSize items

// Computed — derived automatically, no manual subscriptions
totalCount      = computed(() => this.quotes().length);
summary         = computed(() => /* human-readable result summary */);
hasActiveFilter = computed(() => authorSearch or quoteSearch is non-empty);
viewState       = computed<'loading' | 'error' | 'success'>(...);

// Effect — re-fetches whenever page, size, or either search signal changes
effect(() => {
  const page = this.currentPage();   // read = subscribe
  ...
  this.loadQuotes(page, size, author, text);
});
```

## Bugs found and fixed during verification

The API was started and curled to exercise every UI state. Two real bugs were found.

### Bug 1 — Next button never disabled (paginated past end of data)

**Found by:** curling `GET /api/quotes?page=101&size=10` → returns `[]`. The Next button had no `[disabled]` binding, so `nextPage()` kept incrementing the page counter with no guard.

**Proof:**
```
Page 100: 10 rows → hasMore = true  → Next ENABLED   ✓
Page 101:  0 rows → hasMore = false → Next DISABLED   ✓
```

**Fix:**
- Added `hasMore = signal(true)` to the component.
- `loadQuotes` sets `this.hasMore.set(data.length >= size)` after each successful fetch.
- Template: `[disabled]="!hasMore()"` on the Next button.

### Bug 2 — `@empty` block showed wrong message when paginating past the last page

**Found by:** navigating to page 101 (no active filters). The empty state said "No quotes found for your search. Clear all filters" and offered a "Clear all filters" button — but there were no filters to clear. Clicking it did nothing.

**Fix:**
- Added `hasActiveFilter` computed signal.
- `@empty` block now branches with `@if (hasActiveFilter())`:
  - With a filter → "No quotes found for your search. Clear all filters."
  - Without a filter → "You've reached the last page. ← Go back"

### Dead code removed

| Removed | Why |
|---|---|
| `filteredQuotes = computed(() => this.quotes())` | No-op passthrough — added indirection with no filtering logic |
| `pageStart` computed | Declared but never referenced in the template |
| `searchTerm` signal | Redundant mirror of `authorSearch`, set in sync with it everywhere |

## Build

```powershell
npm run build
```

Output goes to `dist/quotes-ui/`. Production build enables full optimization and output hashing.
