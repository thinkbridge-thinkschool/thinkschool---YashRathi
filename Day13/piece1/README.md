# Day 13 – Piece 1: Signals + Zoneless + Standalone (Angular 21)

## Problem Statement

> Angular 21 is signals-first and zoneless. Today you direct the agent (Claude Code) to build it —
> the company's work is directing and verifying AI, not hand-typing components. Brief the agent to
> scaffold a standalone app (no NgModules) against your real Week-1 API: `signal()`/`computed()`/`effect()`
> for state, the new control flow (`@if`/`@for`/`@switch`), and `inject()` over constructor injection.
> Then verify and defend it.

---

## Part 1 — Brief to the Agent

> **Goal:** Build a standalone Angular 21 app (no NgModules, zoneless) against my Week-1 QuotesApi.
>
> **Real API endpoint:**
> `GET /api/quotes?page=1&size=10&author=Einstein&text=imagination`
>
> **Response shape (array of):**
> ```json
> { "id": 1, "author": "Einstein", "text": "...", "createdAt": "2026-05-20T13:24:33+00:00" }
> ```
>
> **Requirements:**
> - `standalone: true` on the root component, no NgModules anywhere
> - Zoneless: register `provideZonelessChangeDetection()` in `app.config.ts`, remove `zone.js`
> - Signals for state: `currentPage`, `pageSize`, `authorSearch`, `quoteSearch`, `quotes`, `isLoading`, `errorMessage`
> - Two `computed()` values derived from multiple signals: `summary` (from `authorSearch` + `quoteSearch` + count) and `viewState` (from `isLoading` + `errorMessage`)
> - One `effect()` that re-fetches data when any of `currentPage`, `pageSize`, `authorSearch`, `quoteSearch` changes
> - New Angular control flow in template: `@if`, `@for (quote of filteredQuotes(); track quote.id)` with `@empty`, `@switch (viewState())` for loading/error/success
> - `inject()` everywhere — no constructor injection in service or component
> - Proxy `/api` → `http://localhost:5000` via `proxy.conf.json`

---

## Part 2 — What Was Built

| File | Purpose |
|------|---------|
| `quotes-ui/src/main.ts` | Bootstrap entry point using `bootstrapApplication` |
| `quotes-ui/src/app/app.config.ts` | `provideZonelessChangeDetection()` + `provideHttpClient()` — no NgModules |
| `quotes-ui/src/app/quote.model.ts` | `Quote` interface matching the real API fields: `id`, `author`, `text`, `createdAt` |
| `quotes-ui/src/app/quotes.service.ts` | Standalone service, `inject(HttpClient)`, builds URL with `encodeURIComponent` |
| `quotes-ui/src/app/app.component.ts` | Root standalone component — all signals, computed, effect |
| `quotes-ui/src/app/app.component.html` | Template with `@if`, `@for` + `@empty`, `@switch` |
| `quotes-ui/proxy.conf.json` | Dev proxy: `/api/*` → `http://localhost:5000` |

---

### Main Dashboard

![Main Dashboard](Screenshot/Screenshot%20(190).png)

### Search & Quotes View

![Search & Quotes View](Screenshot/Screenshot%20(191).png)
![Search & Quotes View](Screenshot/Screenshot%20(192).png)


---

## Key Angular 21 Patterns

### Signals for state

```ts
currentPage  = signal(1);
authorSearch = signal('');
quoteSearch  = signal('');
quotes       = signal<Quote[]>([]);
isLoading    = signal(false);
errorMessage = signal<string | null>(null);
```

Every piece of mutable state is a signal. Setting any of them triggers only the parts of the template
that read it — no global change detection sweep.

---

### `computed()` derived from two signals

```ts
summary = computed(() => {
  const author = this.authorSearch().trim();
  const text   = this.quoteSearch().trim();
  const count  = this.totalCount();
  const page   = this.currentPage();
  if (author && text)  return `Found ${count} quotes by "${author}" containing "${text}" — Page ${page}`;
  if (author)          return `Found ${count} quotes by "${author}" — Page ${page}`;
  if (text)            return `Found ${count} quotes containing "${text}" — Page ${page}`;
  return `Showing ${count} quotes — Page ${page}`;
});

viewState = computed<'loading' | 'error' | 'success'>(() => {
  if (this.isLoading()) return 'loading';
  if (this.errorMessage() !== null) return 'error';
  return 'success';
});
```

`summary` re-computes whenever `authorSearch`, `quoteSearch`, `totalCount`, or `currentPage` changes.
`viewState` drives the `@switch` in the template — a single source of truth for UI state.

---

### `effect()` as the data-loading trigger

```ts
constructor() {
  effect(() => {
    const page   = this.currentPage();
    const size   = this.pageSize();
    const author = this.authorSearch();
    const text   = this.quoteSearch();
    this.loadQuotes(page, size, author, text);
  });
}
```

The effect reads four signals. Angular's reactive graph tracks this automatically — any change to any of
those four signals re-runs the effect and fires a fresh HTTP request. No manual subscriptions, no `ngOnChanges`.

---

### New control flow in the template

```html
<!-- @if — conditional UI -->
@if (authorSearch() || quoteSearch()) {
  <div class="filter-chips"> ... </div>
}

<!-- @switch — state machine -->
@switch (viewState()) {
  @case ('loading') { <!-- spinner --> }
  @case ('error')   { <div class="error-card">...</div> }
  @default          {
    <!-- @for with track + @empty -->
    @for (quote of filteredQuotes(); track quote.id) {
      <article class="quote-card">
        <p>{{ quote.text }}</p>
        <strong>{{ quote.author }}</strong>
        <span>{{ quote.createdAt.slice(0, 10) }}</span>
      </article>
    } @empty {
      <div class="empty-state">No quotes found for your search.</div>
    }
  }
}
```

`track quote.id` tells Angular which DOM node maps to which object — without it, every re-render
destroys and re-creates all nodes even when the data hasn't changed.

---

### `inject()` over constructor injection

```ts
// Service
@Injectable({ providedIn: 'root' })
export class QuotesService {
  private http = inject(HttpClient);   // no constructor
}

// Component
export class AppComponent {
  private quotesService = inject(QuotesService);   // no constructor parameter
}
```

`inject()` works at field-declaration time, keeps the constructor clean, and is the required pattern
in zoneless / signal-based Angular where constructor injection creates ordering issues with effects.

---

### Zoneless config

```ts
// app.config.ts
export const appConfig: ApplicationConfig = {
  providers: [
    provideZonelessChangeDetection(),   // removes Zone.js from the change detection loop
    provideHttpClient(),
  ]
};
```

Without this, Zone.js monkey-patches every async API and schedules a global change detection pass
after each one. With `provideZonelessChangeDetection()`, the scheduler is removed — only signal
writes trigger re-renders, and only in the components that read those specific signals.

---

## Part 3 — Verification Log

### Edges exercised

| State / Edge | How tested | Result |
|---|---|---|
| Normal load | `page=1&size=10`, no filters | 10 quote cards rendered; `summary()` → "Showing 10 quotes — Page 1" |
| `@empty` block | Typed `ZZZNotFound` in Author search | `@for` fell through to `@empty`; "No quotes found" message shown |
| `computed` from two signals | Typed `Einstein` → `summary()` updated. Typed `imagination` in Quote field → `summary()` updated again showing both | Both computed values re-evaluated reactively |
| `effect` re-fires on page change | Clicked Next | Console: `[effect] Fetching quotes — page=2`; new 10 quotes loaded |
| Loading state | Chrome DevTools → Slow 3G; triggered a new search | Spinner banner visible; `viewState()` returned `'loading'` |
| Error state | Stopped backend; typed a new search | Error card appeared with HTTP error message; Try Again button visible |
| Pagination lower bound | `currentPage() === 1`, clicked Previous | Button disabled; `previousPage()` guard blocked the decrement |

---

### Bug caught and fixed

The agent's first `app.config.ts` was missing `provideZonelessChangeDetection()`:

```ts
// Agent's first output — WRONG
export const appConfig: ApplicationConfig = {
  providers: [provideHttpClient()]
};
```

Without it, Zone.js was still in the change detection loop. The symptom: `effect()` fired **twice**
on initial load — once from the signal graph and once from Zone.js patching the HTTP observable.
After adding `provideZonelessChangeDetection()` the double-fire stopped and only signal writes
drove updates.

```ts
// Fixed
export const appConfig: ApplicationConfig = {
  providers: [
    provideZonelessChangeDetection(),
    provideHttpClient(),
  ]
};
```

---

### What would break if the API contract changed

| API change | Breakage | How it fails |
|---|---|---|
| `author` param renamed to `authorName` | Author filter silently stops working | `quotes.service.ts` sends `&author=...`; backend ignores unknown param; returns all quotes with no error |
| `text` param renamed to `content` | Text filter silently stops working | Same silent failure — filter ignored, all quotes returned |
| `id` field renamed to `quoteId` | `track quote.id` tracks `undefined` | Angular loses DOM identity; every fetch destroys and re-creates all cards |
| `createdAt` field removed | `quote.createdAt.slice(0, 10)` throws `TypeError` | Template crash; not caught by `@switch ('error')` — Angular error boundary, not HTTP error |
| Response shape changes from array to `{ items: Quote[], total: number }` | `quotes.set(data)` stores a non-array | `@for` fails to iterate; blank grid with no `@empty` shown |

---

### What zoneless changes about change detection

With **Zone.js** (default), Angular schedules a change detection pass after every async operation
(XHR, setTimeout, Promise, DOM event) because Zone.js patches all of them globally. Every component
in the tree is dirty-checked on every tick regardless of whether its data changed.

With **`provideZonelessChangeDetection()`**, Zone.js is removed from the loop entirely. Change
detection only runs when a signal that a template reads is `.set()` or `.update()`-d. The `effect()`
and `computed()` graph drives all updates — only the components whose signals actually changed are
re-checked. The trade-off: any value outside the signal graph (a plain class field, a non-signal
`@Input()`) will never trigger a re-render automatically; everything that drives the view must be a signal.

---

## Project Structure

```
piece1/
├── quotes-ui/                              ← Angular 21 standalone app
│   ├── proxy.conf.json                     ← /api → http://localhost:5000
│   ├── angular.json                        ← CLI config (proxyConfig wired)
│   ├── src/
│   │   ├── main.ts                         ← bootstrapApplication entry
│   │   └── app/
│   │       ├── app.config.ts               ← provideZonelessChangeDetection()
│   │       ├── app.component.ts            ← signals + computed + effect
│   │       ├── app.component.html          ← @if / @for / @switch
│   │       ├── app.component.css           ← card layout, responsive grid
│   │       ├── quotes.service.ts           ← inject(HttpClient), GET /api/quotes
│   │       └── quote.model.ts              ← { id, author, text, createdAt }
├── Endpoints/
│   └── QuoteEndpoints.cs                   ← GET /api/quotes (backend)
├── Queries/
│   └── GetQuotesQueryHandler.cs            ← EF Core paginated projection
├── appsettings.json
├── Program.cs
└── README.md
```

---

## How to Run

### 1. Start the backend API (Terminal 1)

```powershell
cd "C:\Users\LENOVO\OneDrive\Desktop\Thinkschool\Day13\piece1"
dotnet run
```

API starts at `http://localhost:5000`.

### 2. Start the Angular dev server (Terminal 2)

```powershell
cd "C:\Users\LENOVO\OneDrive\Desktop\Thinkschool\Day13\piece1\quotes-ui"
npm start
```

App starts at `http://localhost:4200`. The proxy forwards all `/api/*` requests to port 5000.

### 3. Verify the API endpoints directly

```powershell
# Page 1 — normal load
curl "http://localhost:5000/api/quotes?page=1&size=10"

# Author filter
curl "http://localhost:5000/api/quotes?page=1&size=10&author=Einstein"

# Both filters
curl "http://localhost:5000/api/quotes?page=1&size=10&author=Einstein&text=imagination"

# Empty result
curl "http://localhost:5000/api/quotes?page=1&size=10&author=ZZZNotFound"
```

### 4. Run backend tests

```powershell
cd "C:\Users\LENOVO\OneDrive\Desktop\Thinkschool\Day13\piece1"
dotnet test
```
