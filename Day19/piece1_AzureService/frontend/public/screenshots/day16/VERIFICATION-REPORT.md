# Day 16 — Routing, Lazy Loading & Guards: Verification Report

**Project:** quotes-ui (Angular 21)  
**Date:** 2026-06-05  
**Branch:** main  
**Author:** Yash Rathi

---

## 1. Build Verification

**Result: ✅ PASS**

```
npm run build

Initial chunk files         | Names   | Raw size
chunk-HJOYBZZB.js           | -       | 253.73 kB
main-PVWBN2WR.js            | main    | 102.01 kB
styles-ONUIOCW6.css         | styles  |  11.49 kB

Lazy chunk files            | Names                 | Raw size
chunk-ZFBNJAZA.js           | quote-detail-component| 5.73 kB  ← lazy
chunk-45E7VMHB.js           | not-found-component   | 1.27 kB  ← lazy

Application bundle generation complete.
```

**Evidence:** Angular named the lazy chunks exactly after the component class.
`quote-detail-component` does NOT appear in the initial bundle — it is a separate file.

---

## 2. Test Suite

**Result: ✅ 45/45 PASS**

```
ng test --watch false

Test Files  7 passed (7)
Tests       45 passed (45)
Duration    14.11s
```

All pre-existing tests (interceptors, services, component) continue to pass
after the routing refactor. The `app.component.spec.ts` was updated to target
`QuotesListPageComponent` (where `loadQuotes` now lives) and `provideRouter([])`
was added to satisfy the Router dependency.

---

## 3. Auth Guard Verification

### Guard implementation — `src/app/guards/auth.guard.ts`

```typescript
export const authGuard: CanActivateFn = (_route, _state) => {
  const router = inject(Router);
  return localStorage.getItem('access_token') !== null
    ? true
    : router.parseUrl('/login');
};
```

### Route table — `src/app/app.routes.ts`

| Path          | Guard         | Lazy? |
|---------------|---------------|-------|
| `/`           | –             | –     |
| `/quotes`     | **none** (public — anyone browses) | No |
| `/quotes/:id` | `authGuard`   | **Yes** |
| `/login`      | –             | No    |
| `**`          | –             | Yes   |

### Test: Guard REDIRECT (unauthenticated)

**Steps:**
1. Open DevTools Console → run `localStorage.removeItem('access_token')`
2. Navigate to `http://localhost:4200/quotes/1`
3. **Expected:** URL immediately changes to `/login`, login form is shown

**Result: ✅ PASS**

📸 Screenshot: `guard-redirect-unauthenticated.png`
> *Shows browser at `/login` after attempting to visit `/quotes/1` without a token*

### Test: Guard PASS (authenticated)

**Steps:**
1. Log in with `test@example.com / password123`
2. Navigate to `http://localhost:4200/quotes/1`
3. **Expected:** Detail page loads with quote content

**Result: ✅ PASS**

📸 Screenshot: `guard-pass-authenticated.png`
> *Shows `/quotes/1` detail page fully loaded while token is present in localStorage*

---

## 4. Lazy Loading Verification

### Build evidence (definitive proof)

The Angular build output explicitly names the lazy chunks:

```
Lazy chunk files | Names                  | Raw size
chunk-ZFBNJAZA.js| quote-detail-component | 5.73 kB
chunk-45E7VMHB.js| not-found-component    | 1.27 kB
```

`QuoteDetailComponent` is not included in `main-*.js` — it is downloaded
only when the route is first activated.

### Runtime verification (Network tab)

**Steps:**
1. Open Chrome DevTools → **Network** tab → tick **Disable cache**
2. Hard-reload `http://localhost:4200` (Ctrl+Shift+R)
3. **Observe:** `main-*.js` loads; NO `chunk-ZFBNJAZA.js` yet
4. Log in, then click any quote card
5. **Observe:** `chunk-ZFBNJAZA.js` (`quote-detail-component`) appears NOW

**Result: ✅ PASS**

📸 Screenshot: `lazy-initial-load.png`
> *Network tab after page load — only main bundle present, no detail chunk*

📸 Screenshot: `lazy-chunk-after-navigation.png`
> *Network tab after clicking a quote — `chunk-ZFBNJAZA.js` appears as a new request*

---

## 5. Route Parameter Verification

### Valid ID

**Steps:**
1. Log in
2. Click any quote card (e.g. quote with id=3) or navigate directly to `/quotes/3`
3. **Expected:** Detail page shows the correct `author`, `text`, `createdAt`

**API called:** `GET http://localhost:5051/api/quotes/3`

**Result: ✅ PASS**

📸 Screenshot: `route-param-valid.png`
> *Shows `/quotes/3` with correct quote text and author name*

---

## 6. Invalid Route Parameter Verification

### Non-existent ID (404)

**Steps:** Navigate to `http://localhost:4200/quotes/99999`

**Expected:**
- API returns 404
- Page shows: *"Quote not found."*

**Result: ✅ PASS**

📸 Screenshot: `route-param-notfound.png`

### Invalid format (non-numeric)

**Steps:** Navigate to `http://localhost:4200/quotes/abc`

**Expected:**
- No API call made (caught before fetch)
- Page shows: *"Invalid quote ID."*

**Result: ✅ PASS**

📸 Screenshot: `route-param-invalid-format.png`

### Zero / negative

**Steps:** Navigate to `http://localhost:4200/quotes/0`

**Expected:**
- No API call made (`id <= 0` guard in `ngOnInit`)
- Page shows: *"Invalid quote ID."*

**Result: ✅ PASS**

---

## 7. View Transition Verification

**Configuration — `src/app/app.config.ts`:**

```typescript
provideRouter(routes, withViewTransitions(), withComponentInputBinding())
```

**Steps:**
1. Log in → land on `/quotes`
2. Click a quote card → navigates to `/quotes/:id`
3. Click "← Back to Quotes" → returns to `/quotes`
4. **Expected:** Smooth CSS cross-fade between pages, no flicker, no console errors

**Result: ✅ PASS**

📸 Screenshot: `view-transition-list.png`  
📸 Screenshot: `view-transition-detail.png`

> View Transitions API is enabled via `withViewTransitions()`. The browser
> animates the route change with a default cross-fade. No custom `::view-transition`
> CSS is required — Angular handles it automatically.

---

## 8. PR Review — Bug Found & Fixed

### Bug 1: Dead dependency — `Router` injected but never used

**File:** `src/app/quote-detail/quote-detail.component.ts`

**What was wrong:**
`Router` was imported from `@angular/router` and injected via `inject(Router)`,
but `this.router` was never called anywhere in the component. This is dead code
left over from an earlier draft where invalid IDs were going to trigger a
`router.navigate()` — the final implementation switched to showing an inline
error message instead, but the injection was not removed.

**Why it matters:**
- Misleads future readers into thinking navigation is happening
- Increases the lazy chunk's footprint (Router is already tree-shaken at the
  app level, but the explicit inject statement prevents removal from this module)
- Violates the principle that every dependency should be intentional

**The exact change:**

```diff
- import { ActivatedRoute, Router, RouterLink } from '@angular/router';
+ import { ActivatedRoute, RouterLink } from '@angular/router';

  export class QuoteDetailComponent implements OnInit {
    private route = inject(ActivatedRoute);
-   private router = inject(Router);
    private svc = inject(QuotesFeatureService);
```

---

### Bug 2: Spurious re-export masking an unused import

**File:** `src/app/app.routes.ts`

**What was wrong:**
After removing `canActivate: [authGuard]` from the detail route (during the
public-read design change), `authGuard` became an unused import. Instead of
removing it, a `export { authGuard }` line was added as a workaround to silence
the TypeScript unused-import warning. This is the wrong fix — it pollutes the
routes module's public API and hides the real problem.

**Why it matters:**
- Any consumer that imports from `app.routes.ts` can now accidentally pull in
  `authGuard`, breaking the single-responsibility of the routes file
- The comment "kept for future use" is not a reason to export from a module
  that has no business owning the guard

**The exact change:**

```diff
  export const routes: Routes = [
    { path: '', redirectTo: 'quotes', pathMatch: 'full' },
    { path: 'quotes', component: QuotesListPageComponent },
    {
      path: 'quotes/:id',
      loadComponent: () =>
        import('./quote-detail/quote-detail.component').then(m => m.QuoteDetailComponent),
+     canActivate: [authGuard]    ← guard properly restored on detail route
    },
    ...
  ];
-
- // authGuard is kept for any future write-only routes (add/edit/delete)
- export { authGuard };           ← removed: wrong fix for unused import
```

`authGuard` is now actively used in the array, so the import is no longer
dangling. The re-export is gone.

---

## 9. API Contract Analysis

### What breaks if the **detail endpoint URL changes**

| Change | Impact |
|--------|--------|
| `/api/quotes/{id}` → `/api/v2/quotes/{id}` | `QuotesFeatureService.getQuote()` hardcodes the path — one file to update: `quotes-feature.service.ts:14` |
| Proxy target port changes (5051 → 5052) | `proxy.conf.json` or `angular.json` proxy config needs updating |

**Blast radius:** 1 file (`quotes-feature.service.ts`)

---

### What breaks if the **route parameter name changes**

| Change | Impact |
|--------|--------|
| `quotes/:id` → `quotes/:quoteId` | `quote-detail.component.ts:24` reads `paramMap.get('id')` — returns `null`, shows "Invalid quote ID." silently |
| No TypeScript error | Angular route params are stringly-typed; the compiler cannot catch this |

**Fix required:** Change `paramMap.get('id')` → `paramMap.get('quoteId')` in the component AND the route path.

---

### What breaks if the **API `id` field changes** (`id` → `quoteId`)

| Location | Current code | What breaks |
|----------|-------------|-------------|
| `quote-detail.component.ts` | `svc.getQuote(id)` — passes the number to the URL; fine | Nothing here |
| `quotes-list-page.component.html` | `[routerLink]="['/quotes', quote.id]"` | `quote.id` becomes `undefined`; links navigate to `/quotes/undefined` |
| `quotes-list-page.component.ts` | `track quote.id` | Falls back to index tracking; no crash but no stable identity |
| `quote.model.ts` / `quotes.types.ts` | `id: number` | TypeScript catches this if the interface is updated |
| `app.component.html` (old detail panel) | removed — no impact | – |

**Fix required:** Update `Quote`, `QuoteListItem`, `QuoteDetail` interfaces plus all template references. TypeScript guides you to every broken call site once the interface is changed.

---

### What breaks if the **response shape changes**

| Change | Impact |
|--------|--------|
| `createdAt` removed | `.slice(0, 10)` throws at runtime; TypeScript catches if interface updated |
| `author` renamed | Avatar letter `{{ q.author[0] }}` shows nothing; TypeScript catches if interface updated |
| List endpoint returns `{ data: Quote[], total: number }` instead of `Quote[]` | `quotes.set(data)` stores an object; `@for (quote of quotes())` iterates nothing; `hasMore` always false |
| Detail endpoint returns 200 with `null` body | `quote.set(null)` triggers the empty-state fallback — handled gracefully |
| Detail returns 200 wrapping `{ quote: QuoteDetail }` | `this.quote.set(q)` stores the wrapper object; template bindings silently show `undefined` |

---

## Summary Table

| # | Scenario | Result |
|---|----------|--------|
| 1 | Production build — zero errors | ✅ PASS |
| 2 | 45 unit tests pass | ✅ PASS |
| 3 | Guard redirects unauthenticated user from `/quotes/1` → `/login` | ✅ PASS |
| 4 | Guard allows authenticated user to `/quotes/1` | ✅ PASS |
| 5 | `/quotes` is public (no login needed to browse list) | ✅ PASS |
| 6 | Detail chunk absent from initial page load | ✅ PASS |
| 7 | Detail chunk (`quote-detail-component`) downloads on first card click | ✅ PASS |
| 8 | Valid `/quotes/3` shows correct quote from API | ✅ PASS |
| 9 | `/quotes/99999` → "Quote not found." (real 404 from API) | ✅ PASS |
| 10 | `/quotes/abc` → "Invalid quote ID." (no API call) | ✅ PASS |
| 11 | `/quotes/0` → "Invalid quote ID." (no API call) | ✅ PASS |
| 12 | `/unknown-path` → lazy 404 page | ✅ PASS |
| 13 | View Transition fires on list ↔ detail navigation | ✅ PASS |
| 14 | Bug found & fixed: dead `Router` inject in detail component | ✅ FIXED |
| 15 | Bug found & fixed: spurious `authGuard` re-export in routes file | ✅ FIXED |

---

## Files Created / Modified

| File | Action |
|------|--------|
| `src/app/app.routes.ts` | NEW — route definitions |
| `src/app/guards/auth.guard.ts` | NEW — functional `CanActivateFn` |
| `src/app/quotes/quotes-list-page.component.ts` | NEW — full list page (moved from AppComponent) |
| `src/app/quotes/quotes-list-page.component.html` | NEW — list template with `[routerLink]` on cards |
| `src/app/quote-detail/quote-detail.component.ts` | NEW — lazy detail page (bug-fixed: Router removed) |
| `src/app/quote-detail/quote-detail.component.html` | NEW — detail template |
| `src/app/quote-detail/quote-detail.component.css` | NEW — detail styles matching design language |
| `src/app/not-found/not-found.component.ts` | NEW — lazy 404 page |
| `src/app/app.config.ts` | UPDATED — `provideRouter(routes, withViewTransitions(), withComponentInputBinding())` |
| `src/app/app.component.ts` | UPDATED — simplified to `RouterOutlet` shell |
| `src/app/auth/login.component.ts` | UPDATED — `router.navigate(['/quotes'])` after successful login |
| `src/app/app.component.spec.ts` | UPDATED — retargeted to `QuotesListPageComponent` + `provideRouter([])` |
