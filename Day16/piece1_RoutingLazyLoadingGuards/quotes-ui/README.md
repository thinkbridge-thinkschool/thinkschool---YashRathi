# quotes-ui — Day 16: Routing, Lazy Loading & Guards

Angular 21 frontend for the Week-1 .NET QuotesAPI.
Builds on Day 15's HttpClient + Interceptors work by adding client-side routing,
lazy-loaded route chunks, a functional auth guard, and View Transitions.

---

## Prerequisites

| Tool | Version |
|------|---------|
| Node | 20+ |
| Angular CLI | 21+ |
| .NET backend | running on `http://localhost:5051` |

---

## Running the app

```bash
# install dependencies (first time only)
npm install

# start dev server with proxy to .NET API
npm start
# → http://localhost:4200
```

```bash
# run all unit tests (Vitest, watch=false)
ng test --watch false

# production build — confirms lazy chunks are named
npm run build
```

---

## Routes

| URL | Component | Lazy? | Guard |
|-----|-----------|-------|-------|
| `/` | redirect → `/quotes` | — | — |
| `/quotes` | `QuotesListPageComponent` | No | None (public) |
| `/quotes/:id` | `QuoteDetailComponent` | **Yes** | `authGuard` |
| `/login` | `LoginComponent` | No | — |
| `/**` | `NotFoundComponent` | **Yes** | — |

Anyone can browse the quotes list without logging in.
Viewing a quote's detail page requires a valid JWT in `localStorage['access_token']`.

---

## Key files

| File | Purpose |
|------|---------|
| `src/app/app.routes.ts` | Route table with `loadComponent` for lazy routes |
| `src/app/guards/auth.guard.ts` | Functional `CanActivateFn` — checks localStorage, redirects to `/login` |
| `src/app/app.config.ts` | `provideRouter(routes, withViewTransitions(), withComponentInputBinding())` |
| `src/app/quotes/quotes-list-page.component.ts` | Public list page with search + pagination |
| `src/app/quote-detail/quote-detail.component.ts` | Protected detail page — lazy chunk |
| `src/app/not-found/not-found.component.ts` | 404 fallback — lazy chunk |

---

## Architecture decisions

**Why AppComponent is just `<router-outlet />`**
A component cannot be both the application bootstrap root and a named route target.
The original AppComponent content (quotes list, pagination, search) was extracted into
`QuotesListPageComponent`, and AppComponent became a minimal shell.

**Why the detail route is protected but the list route is not**
Public-read / protected-write pattern:
browsing is marketing; forcing login before reading pushes users away.
Only actions that mutate data (add/edit/delete) or reveal deeper content (full quote detail)
require authentication.

**Why `withViewTransitions()`**
Enables the browser's native View Transitions API for route changes.
Angular handles the cross-fade automatically — no custom `::view-transition` CSS required.

---

## Test credentials

```
email:    test@example.com
password: password123
```

---

## Build output (lazy chunks confirmed)

```
Initial chunk files         | Names   | Raw size
main-PVWBN2WR.js            | main    | 102.01 kB

Lazy chunk files            | Names                  | Raw size
chunk-ZFBNJAZA.js           | quote-detail-component | 5.73 kB
chunk-45E7VMHB.js           | not-found-component    | 1.27 kB
```

`QuoteDetailComponent` is absent from the initial bundle —
it is downloaded only when the user first navigates to `/quotes/:id`.

---

## Test results

```
ng test --watch false

Test Files  7 passed (7)
Tests       45 passed (45)
Duration    ~14s
```
