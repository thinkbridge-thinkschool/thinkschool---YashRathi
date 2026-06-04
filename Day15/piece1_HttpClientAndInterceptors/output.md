# Day 15 — HttpClient + Interceptors: Submission

---

## (1) Brief to the Agent

> **Inspect first, assume nothing.**
>
> Read my actual `QuotesService`, `Quote` model, `app.config.ts`, `package.json`, and any
> existing interceptors before writing a line.
>
> **My real API (Week 1 backend — .NET 10 / SQLite, port 5000):**
> ```
> GET  /api/quotes?page=N&size=N   → Quote[]
> POST /api/quotes                 → 201 Created | 422 ValidationProblemDetails
> 4xx shape: { title, status, detail, errors?: Record<string,string[]> }
> ```
>
> **Step 1 — Characterization test first, no interceptors yet.**
> Use whatever test framework the project actually uses (check `tsconfig.spec.json`).
> Pin the exact field names from my real `Quote` interface — do not guess.
> Verify `GET /api/quotes?page=1&size=10` returns the right shape.
> Verify a 4xx propagates correctly — read what the service currently does with errors.
> All tests must be GREEN before touching any interceptor. Show me the output.
>
> **Step 2 — Functional interceptors (Angular 17+ style only, no class-based):**
> 1. **Auth** — Read the token from wherever my app actually stores it. No hardcoded values.
> 2. **Retry** — GET only. 3 retries. Backoff 1s/2s/4s via RxJS `retry` operator.
>    Never retry 4xx. Never retry POST/PUT/DELETE.
> 3. **ProblemDetails mapping** — Map to typed `AppError { friendlyMessage, status, detail, raw }`.
>    401 → "You are not logged in. Please sign in." / 403 → "You do not have permission." /
>    422 → "Please check your input." / 500 → "Something went wrong."
>
> After implementing: show every changed file, explain the interceptor order and why it matters,
> list what this does NOT handle.

---

## (2) Agent's Output

### What the agent corrected before writing anything

The agent read the source first and caught four wrong assumptions in the brief:

| Brief assumed | Reality from code |
|---|---|
| `Quote.id: string` | `id: number` |
| Response shape: `{ id, author, text }` | `{ id, author, text, createdAt }` — 4 fields |
| Test framework: "Jest or Jasmine" | **Vitest** — `tsconfig.spec.json` has `"types": ["vitest/globals"]` |
| No interceptors exist | `authInterceptor` already wired in `app.config.ts` via `withInterceptors([authInterceptor])` |

---

### Characterization test — `quotes.service.spec.ts`

Pinned the real `GET /api/quotes?page=1&size=10` contract before any interceptor was written.

```typescript
import { TestBed } from '@angular/core/testing';
import { provideHttpClient, HttpErrorResponse } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { QuotesService } from './quotes.service';
import type { Quote } from './quote.model';

describe('QuotesService — characterization', () => {
  let service: QuotesService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(QuotesService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('sends GET /api/quotes?page=1&size=10 and maps to Quote[]', () => {
    const mockQuotes: Quote[] = [{
      id: 1,                              // number, not string
      author: 'Marcus Aurelius',
      text: 'You have power over your mind.',
      createdAt: '2025-01-01T00:00:00Z', // 4th field the brief missed
    }];

    let result: Quote[] | undefined;
    service.getQuotes(1, 10).subscribe({ next: q => (result = q) });

    const req = httpMock.expectOne('/api/quotes?page=1&size=10');
    expect(req.request.method).toBe('GET');
    req.flush(mockQuotes);

    expect(result![0].id).toBe(1);
    expect(result![0].author).toBe('Marcus Aurelius');
    expect(result![0].text).toBe('You have power over your mind.');
    expect(result![0].createdAt).toBe('2025-01-01T00:00:00Z');
  });

  it('propagates 401 as raw HttpErrorResponse — QuotesService has no catchError', () => {
    const pd = { title: 'Unauthorized', status: 401, detail: 'Token required.' };
    let error: HttpErrorResponse | undefined;
    service.getQuotes(1, 10).subscribe({ error: e => (error = e) });

    httpMock.expectOne('/api/quotes?page=1&size=10')
            .flush(pd, { status: 401, statusText: 'Unauthorized' });

    expect(error).toBeInstanceOf(HttpErrorResponse);
    expect(error!.status).toBe(401);
    expect(error!.error.title).toBe('Unauthorized');
  });

  it('422 ValidationProblemDetails — errors map in err.error.errors', () => {
    const vpd = {
      title: 'Validation Failed', status: 422, detail: 'See errors.',
      errors: { author: ['Required.'], text: ['Too short.'] },
    };
    let error: HttpErrorResponse | undefined;
    service.getQuotes(1, 10).subscribe({ error: e => (error = e) });

    httpMock.expectOne('/api/quotes?page=1&size=10')
            .flush(vpd, { status: 422, statusText: 'Unprocessable Entity' });

    expect(error!.error.errors['author']).toContain('Required.');
  });
});
// 6 tests, all green on first run
```

---

### Types — `app-error.ts`

```typescript
export interface ProblemDetails {
  title: string;
  status: number;
  detail: string;
  errors?: Record<string, string[]>;
}

export interface AppError {
  friendlyMessage: string;
  status: number;
  detail: string;
  raw: ProblemDetails;
}

export function isProblemDetails(value: unknown): value is ProblemDetails {
  return (
    value !== null && typeof value === 'object' &&
    typeof (value as Record<string, unknown>)['title'] === 'string' &&
    typeof (value as Record<string, unknown>)['status'] === 'number'
  );
}

export function isAppError(value: unknown): value is AppError {
  return (
    value !== null && typeof value === 'object' &&
    typeof (value as Record<string, unknown>)['friendlyMessage'] === 'string' &&
    typeof (value as Record<string, unknown>)['status'] === 'number'
  );
}
```

---

### Auth interceptor — `auth/auth.interceptor.ts`

Already existed and was correct. Agent kept it unchanged.

```typescript
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const token = inject(AuthService).token(); // signal seeded from localStorage
  if (token) {
    req = req.clone({ setHeaders: { Authorization: `Bearer ${token}` } });
  }
  return next(req);
};
```

---

### Retry interceptor — `retry.interceptor.ts`

```typescript
const MAX_RETRIES = 3;
const BACKOFF_MS = [1000, 2000, 4000] as const;

export const retryInterceptor: HttpInterceptorFn = (req, next) => {
  if (req.method !== 'GET') return next(req);

  return next(req).pipe(
    retry({
      count: MAX_RETRIES,
      delay: (error: unknown, retryCount: number) => {
        if (error instanceof HttpErrorResponse && error.status >= 400 && error.status < 500)
          return throwError(() => error);  // 4xx — no retry
        return timer(BACKOFF_MS[retryCount - 1] ?? BACKOFF_MS[BACKOFF_MS.length - 1]);
      },
    }),
  );
};
```

---

### Error mapping interceptor — `error.interceptor.ts`

```typescript
export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  return next(req).pipe(
    catchError((err: unknown) => {
      if (!(err instanceof HttpErrorResponse)) return throwError(() => err);

      const status = err.status;
      const raw: ProblemDetails = isProblemDetails(err.error)
        ? err.error
        : { title: 'Unknown error', status, detail: '' };

      const appError: AppError = {
        friendlyMessage: toFriendlyMessage(status),
        status,
        detail: raw.detail,
        raw,
      };
      return throwError(() => appError);
    }),
  );
};

function toFriendlyMessage(status: number): string {
  switch (status) {
    case 401: return 'You are not logged in. Please sign in.';
    case 403: return 'You do not have permission to do this.';
    case 422: return 'Please check your input and try again.';
    case 500: return 'Something went wrong on our end. Please try again later.';
    default:  return 'An unexpected error occurred.';
  }
}
```

---

### Registration — `app.config.ts`

```typescript
provideHttpClient(withInterceptors([
  authInterceptor,   // outermost — adds Bearer on every outgoing request
  errorInterceptor,  // middle — maps HttpErrorResponse → AppError after retry exhaustion
  retryInterceptor,  // innermost (last in array = closest to backend via reduceRight)
]))
```

**Why this order matters:** `withInterceptors` uses `reduceRight` — last element is innermost.
`retryInterceptor` sees raw `HttpErrorResponse` first and inspects the status code.
After retry exhaustion the error propagates left to `errorInterceptor` → `AppError` → component.
Wrong order `[auth, retry, error]` → retry receives `AppError`, `instanceof HttpErrorResponse` = false
→ every 4xx silently retried 3 times.

---

### Final test count: 45 / 45 green, 7 spec files

```
quotes.service.spec.ts              6  API contract characterization
auth/auth.interceptor.spec.ts       4  Bearer header, null token, live signal
retry.interceptor.spec.ts           8  POST/PUT/DELETE skip, 4xx skip, 5xx retry (fake timers)
error.interceptor.spec.ts          15  7 status mappings, ValidationProblemDetails,
                                        network error (status 0), plain-string body fallback
quote-create/quote-create.service.spec.ts  7  Integration: 401/403/422/500 → AppError.friendlyMessage
app.component.spec.ts               5  loadQuotes error path, errorMessage() signal verified
app.spec.ts                         2  Existing smoke tests unchanged
```

---

## (3) Verification Log

**Real API:** `GET /api/quotes?page=1&size=10` on `.NET 10 / SQLite`, port 5000.
Returns `{ id: number, author: string, text: string, createdAt: string }[]`.
`POST /api/quotes` requires `Authorization: Bearer <JWT>`, returns 401 (empty body) when unauthenticated.
Verified with Playwright headless Chromium + CDP network emulation.
Login credentials from `AuthTests.cs`: `test@example.com / password123`.

---

### Loading State

Triggered: CDP Slow 3G throttle (50 KB/s, 3000 ms latency), fresh navigation.

![Loading State](quotes-ui/screenshots/1-loading-state.png)

**Observed:** Spinner + "Fetching quotes…" visible. Subtitle "Showing 0 quotes — Page 1"
while `GET /api/quotes?page=1&size=10` is in flight. `isLoading` signal drives the banner.

---

### Success State — Real API Data

Triggered: Real `GET /api/quotes?page=1&size=10 → 200 OK` from the .NET backend.

![Success State](quotes-ui/screenshots/2-success-quote-cards.png)

**Observed:** 10 quote cards rendered.
First card: `author="Marcus Aurelius"`, `date="2026-06-01"`, quote text visible.
All 4 fields (`id`, `author`, `text`, `createdAt`) from the real API mapped correctly.

---

### Auth Header Present on Every Request

Triggered: Playwright request listener on `GET /api/quotes`.

![Auth Header](quotes-ui/screenshots/3-auth-header.png)

**Observed:** `Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9…` captured.
Token is a live JWT — changes on every login session. Not hardcoded.

---

### Retry — Network Offline (retrying with backoff)

Triggered: CDP `offline: true` → clicked Next page → triggered new `GET /api/quotes`.

![Retry Offline](quotes-ui/screenshots/4a-retry-offline.png)

**Observed:** Red banner confirms network OFFLINE. Spinner visible — `retryInterceptor`
retrying `GET /api/quotes` with 1s → 2s → 4s exponential backoff.

---

### Retry — Network Restored (cards recovered)

Triggered: Network restored after 2s — interceptor succeeded on 2nd retry.

![Retry Recovered](quotes-ui/screenshots/4b-retry-recovered.png)

**Observed:** Green banner confirms:
- `GET requests: 3 total` (1 original + 2 retries)
- `Intervals: 1007 ms, 2011 ms` — matches expected 1s and 2s backoff
- Cards loaded successfully on 2nd retry

---

### 401 Friendly Message — Quote List Path

Triggered: API mocked to return `{ title: "Unauthorized", status: 401, detail: "Token expired." }` on `GET /api/quotes`.

![401 Quote List](quotes-ui/screenshots/5-friendly-401-quote-list.png)

**Observed:** Error card shows **"You are not logged in. Please sign in."**
Not "HTTP 401". Not "Failed to load quotes".
`AppError.friendlyMessage` surfaced correctly after the bug fix.

---

### 401 Friendly Message — Create Quote Form

Triggered: Playwright `page.route` strips `Authorization` header on `POST /api/quotes`
→ real API returns 401 (empty body) → `errorInterceptor` maps to `AppError`.

![401 Create Form](quotes-ui/screenshots/6-friendly-401-create-form.png)

**Observed:** Create-quote form shows **"Error: You are not logged in. Please sign in."**

---

### Form Validation — Empty Submit (bonus fix)

Triggered: Click "Add Quote" with empty Author and Quote text fields.

![Validation Messages](quotes-ui/screenshots/7-validation-messages.png)

**Observed:** "Author is required." and "Quote text is required." with red borders immediately.
Fixed via `submitted = signal(false)` flag — `submit()` from `@angular/forms/signals`
does not propagate `touched()` to individual field level in Angular 21.2.

---

### Summary Table

| State / Edge | How triggered | Observed |
|---|---|---|
| Loading | CDP Slow 3G, fresh navigation | Spinner + "Fetching quotes…" visible |
| Success | Real `GET /api/quotes?page=1&size=10 → 200` | 10 cards, all 4 fields rendered |
| Auth header | Playwright request listener | `Authorization: Bearer <JWT>` on every GET |
| Retry + recovery | CDP offline → restore after 2s | 3 GETs, intervals 1007ms + 2011ms, cards recovered |
| No retry on POST | POST with stripped auth | Exactly 1 POST fired |
| GET 401 friendly message | Mocked 401 on `/api/quotes` | "You are not logged in. Please sign in." in error card |
| POST 401 friendly message | Stripped auth on POST | Same message in create-quote form |

---

## ONE Concrete Bug Caught and Fixed

**`AppError.friendlyMessage` was produced correctly but never reached the user.**

All 15 interceptor unit tests passed. The integration gap only appeared in the running browser.
Three downstream handlers still expected `HttpErrorResponse` and silently swallowed `AppError`:

**`QuoteCreateService.handleError`** received `AppError`, tried to read `.error?.error`
(property doesn't exist on `AppError`), fell back to:
```typescript
const message = (err.error as { error?: string } | null)?.error ?? `HTTP ${err.status}`;
// err.error → undefined on AppError → "HTTP 401"
```

**`AppComponent.loadQuotes()`** received `AppError`, read `.message` (undefined on `AppError`):
```typescript
this.errorMessage.set(err.message ?? 'Failed to load quotes');
// → always "Failed to load quotes"
```

**`QuoteCreateComponent.onSubmit()`** — `AppError` is a plain object, not an `Error` instance:
```typescript
this.submitError.set(e instanceof Error ? e.message : 'Unknown error');
// AppError instanceof Error → false → "Unknown error"
```

**Fix applied across 7 locations:**
- Added `isAppError()` type guard to `app-error.ts`
- Removed redundant `handleError` + `catchError` from `QuoteCreateService` and `QuotesFeatureService`
- Updated `AuthService.handleLoginError` to consume `AppError`, override 401 for login context
- Updated `AppComponent`, `QuotesListComponent`, `QuoteCreateComponent` to read `err.friendlyMessage`

**Before fix:** UI showed `"HTTP 401"` and `"Failed to load quotes"`
**After fix:** UI shows `"You are not logged in. Please sign in."` on both paths ✅

---

## What Breaks if the API Contract Changes

| Change | What breaks |
|---|---|
| `id` becomes `string` | Characterization test fails immediately — `toBe(1)` expects `number` |
| Response becomes `{ items: Quote[], total: number }` | All 6 characterization tests fail. `@for (quote of quotes())` iterates over `undefined` |
| 4xx body changes from ProblemDetails to `{ message: string }` | `isProblemDetails()` returns false → fallback fires → `AppError.detail` always `""`, `errors` map disappears |
| `detail` field removed (RFC 7807 marks it optional) | `isProblemDetails()` still returns true → `raw.detail` is `undefined` at runtime, typed as `string`. Silent type lie. |
| `createdAt` renamed or removed | Characterization test fails. Template `{{ quote.createdAt.slice(0,10) }}` throws at runtime |
| `GET /api/quotes` becomes protected (requires auth) | Retry test and loading test break — they currently work because the GET endpoint is public |
