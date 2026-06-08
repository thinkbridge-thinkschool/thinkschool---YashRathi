# quotes-ui — Day 16: State Management (Signals First)

Angular 21 frontend for the Week-1 .NET QuotesAPI.
Builds on Day 15's HttpClient + Interceptors work by introducing a
signal-based centralized store for the quotes feature — all state in one
place, components read-only, mutations only through store actions.

---

## Prerequisites

| Tool | Version |
|------|---------|
| Node | 20+ |
| Angular CLI | 21+ |
| .NET backend | running on `http://localhost:5000` |

---

## Running the app

```bash
# Terminal 1 — start the .NET API (Day16/piece1_RoutingLazyLoadingGuards)
cd ../piece1_RoutingLazyLoadingGuards
dotnet run
# API starts at http://localhost:5000

# Terminal 2 — start the Angular app
npm install        # first time only
npm start
# → http://localhost:4200
# Angular proxies /api/* → http://localhost:5000 via proxy.conf.json
```

```bash
# type-check only (no emit)
npx tsc --project tsconfig.app.json --noEmit

# production build
npm run build
```

---

## Real API endpoints

| Method | URL | Body | Returns |
|--------|-----|------|---------|
| GET | `/api/quotes?page=N&size=N` | — | `Quote[]` |
| GET | `/api/quotes/{id}` | — | `Quote` |
| POST | `/api/quotes` | `{ author, text }` | `201 Quote` |
| DELETE | `/api/quotes/{id}` | — | `204` |

`Quote` shape: `{ id: number, author: string, text: string, createdAt: string }`

---

## Key files

| File | Purpose |
|------|---------|
| `src/app/stores/quotes.store.ts` | Signal store — single source of truth for all quotes state |
| `src/app/quote.model.ts` | `Quote` interface matching the real API shape |
| `src/app/quotes/quotes-list.component.ts` | Wired to `QuotesStore` — calls `store.loadQuotes()`, reads `store.isLoading()` / `store.quotes()` / `store.error()` |
| `src/app/quotes/quotes-list.component.html` | Loading indicator driven by `store.isLoading()` |
| `src/app/quotes/quotes-list-page.component.ts` | Page-level component with search + pagination (uses `QuotesService` directly) |

---

## Store design

### State signals (private, write-sealed)

```typescript
private readonly _quotes        = signal<Quote[]>([]);
private readonly _selectedQuote = signal<Quote | null>(null);
private readonly _isLoading     = signal(false);
private readonly _error         = signal<string | null>(null);
private readonly _currentPage   = signal(1);
private readonly _pageSize      = signal(10);
```

### Public readonly projections

```typescript
readonly quotes        = this._quotes.asReadonly();
readonly selectedQuote = this._selectedQuote.asReadonly();
readonly isLoading     = this._isLoading.asReadonly();
readonly error         = this._error.asReadonly();
readonly currentPage   = this._currentPage.asReadonly();
readonly pageSize      = this._pageSize.asReadonly();
```

### Computed (auto-derived)

```typescript
readonly totalCount = computed(() => this._quotes().length);
readonly hasError   = computed(() => this._error() !== null);
readonly isEmpty    = computed(() => !this._isLoading() && this._quotes().length === 0);
```

### Actions

| Method | HTTP | Behaviour |
|--------|------|-----------|
| `loadQuotes()` | GET `/api/quotes?page&size` | Sets `isLoading`, fetches list, updates `_quotes` |
| `loadQuote(id)` | GET `/api/quotes/{id}` | Fetches one quote, updates `_selectedQuote` |
| `addQuote(author, text)` | POST then GET | POST → `switchMap` to GET list in one chain — no `isLoading` flash |
| `deleteQuote(id)` | DELETE `/api/quotes/{id}` | Confirmed removal: filters `_quotes` only after 204 |
| `setPage(page)` | — | Updates `_currentPage` signal |
| `clearError()` | — | Resets `_error` to null |

---

## Architecture decisions

**Why a centralized store instead of component-local signals**

The existing `QuotesListPageComponent` manages its own local signals — fine for a single component. The store is the next step: one service owns the truth, multiple components can read it, and mutations are only possible through named actions. Components cannot call `.set()` directly because all public projections are `.asReadonly()`.

**Why `switchMap` in `addQuote`**

Calling `store.loadQuotes()` inside a `tap` after the POST would create two separate observables. The POST's `finalize` would set `isLoading(false)` while the follow-up GET was still in-flight — a brief flash of `isLoading = false` in the middle of a logically single operation. `switchMap` chains POST → GET into one observable so `finalize` fires only after both settle.

**Why `deleteQuote` is confirmed, not optimistic**

`tap()` runs on the emitted response value — after the server returns 204. The quote is never removed before the request. A true optimistic approach would snapshot the list, remove immediately, and restore on error. That rollback pattern is one of the explicit signals to migrate to NgRx Effects.

**When to move to NgRx**

Keep this signal store until any one of:
- A second feature needs to react to the same mutation event (cross-feature shared state)
- Any action chains more than two HTTP calls
- Team size reaches 5+ active contributors

---

## Verification checks

```powershell
# Run all checks from quotes-ui/
Select-String "_quotes|_selectedQuote|_isLoading|_error|_currentPage|_pageSize" src/app/stores/quotes.store.ts | Measure-Object | Select-Object Count  # → 6
Select-String "asReadonly" src/app/stores/quotes.store.ts | Measure-Object | Select-Object Count                                                       # → 6
Select-String "totalCount|hasError|isEmpty" src/app/stores/quotes.store.ts | Measure-Object | Select-Object Count                                       # → 3
Select-String "constructor" src/app/stores/quotes.store.ts                                                                                              # → empty (PASS)
Select-String "store.isLoading" src/app/quotes/quotes-list.component.html                                                                               # → line 6
npx tsc --project tsconfig.app.json --noEmit                                                                                                            # → no output (PASS)
```

---

## Screenshots

### Store file — NgRx threshold rule visible, tsc passing

![Store file open in VS Code with NgRx threshold rule comment at top, tsc --noEmit passing in terminal](../screenshots/02-store-ngrx-rule.png)

---

### Loading state — store.isLoading() driving the template

![Browser at localhost:4200/quotes showing "Fetching quotes…" text while GET /api/quotes is in-flight in the Network tab](../screenshots/03-browser-loading-state.png)

---

### TypeScript strict compile — zero errors

![Terminal showing tsc --noEmit with no output (zero errors) and Angular build completing cleanly](../screenshots/01-tsc-zero-errors.png)

---

## Test credentials

```
email:    test@example.com
password: password123
```
