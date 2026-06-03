# Day 13 – Piece 2: Solution

---

## 1. Brief Given to the Agent

> Build a quotes list+detail component against my real Week-1 API:
>
> **Endpoints:**
> - `GET /api/quotes?page=1&size=10` — returns a flat `QuoteListItem[]` array (no wrapper object)
> - `GET /api/quotes/:id` — returns the same shape; on error returns `{ error: string; status: number }`
>
> **Exact field names (do not guess):**
> - `id: number`
> - `author: string`
> - `text: string` — NOT `content`
> - `createdAt: string` — NOT `created_at`
>
> **Requirements:**
> - Angular 21 standalone component, selector `app-quotes-list`, imports `[DatePipe]`
> - Use `signal()` for all state: `quotes`, `detail`, `selectedId`, `loadingList`, `loadingDetail`, `listError`, `detailError`
> - Use `computed()` for `isEmpty` (derived from loading + error + quotes length)
> - Use `inject()` everywhere — no constructor parameters in service or component
> - Wire a `Subject<number>` + `switchMap` so rapid clicks cancel stale detail requests
> - Error extraction: `err.error.error` if present, fall back to `HTTP ${err.status}`
> - No `any` anywhere except the single cast `(err.error as { error?: string } | null)`

---

## 2. Agent's Output

### quotes.types.ts

```ts
export interface QuoteListItem {
  id: number;
  author: string;
  text: string;
  createdAt: string;
}

export interface QuoteDetail extends QuoteListItem {}
```

### quotes-feature.service.ts

```ts
import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Observable, throwError } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { QuoteListItem, QuoteDetail } from './quotes.types';

@Injectable({ providedIn: 'root' })
export class QuotesFeatureService {
  private http = inject(HttpClient);

  listQuotes(page = 1, size = 10): Observable<QuoteListItem[]> {
    return this.http
      .get<QuoteListItem[]>(`/api/quotes?page=${page}&size=${size}`)
      .pipe(catchError(this.handleError));
  }

  getQuote(id: number): Observable<QuoteDetail> {
    return this.http
      .get<QuoteDetail>(`/api/quotes/${id}`)
      .pipe(catchError(this.handleError));
  }

  private handleError(err: HttpErrorResponse): Observable<never> {
    const message: string =
      (err.error as { error?: string } | null)?.error ?? `HTTP ${err.status}`;
    return throwError(() => new Error(message));
  }
}
```

### quotes-list.component.ts

```ts
import {
  Component, OnInit, inject, signal, computed, DestroyRef,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subject, switchMap } from 'rxjs';
import { QuotesFeatureService } from './quotes-feature.service';
import { QuoteListItem, QuoteDetail } from './quotes.types';

@Component({
  selector: 'app-quotes-list',
  standalone: true,
  imports: [DatePipe],
  templateUrl: './quotes-list.component.html',
})
export class QuotesListComponent implements OnInit {
  private svc = inject(QuotesFeatureService);
  private destroyRef = inject(DestroyRef);

  quotes        = signal<QuoteListItem[]>([]);
  selectedId    = signal<number | null>(null);
  detail        = signal<QuoteDetail | null>(null);
  loadingList   = signal(false);
  loadingDetail = signal(false);
  listError     = signal<string | null>(null);
  detailError   = signal<string | null>(null);

  isEmpty = computed(
    () => !this.loadingList() && !this.listError() && this.quotes().length === 0
  );

  private select$ = new Subject<number>();

  constructor() {
    this.select$
      .pipe(
        switchMap((id) => {
          this.loadingDetail.set(true);
          this.detailError.set(null);
          this.detail.set(null);
          return this.svc.getQuote(id);
        }),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: (q) => {
          this.detail.set(q);
          this.loadingDetail.set(false);
        },
        error: (e: Error) => {
          this.detailError.set(e.message);
          this.loadingDetail.set(false);
        },
      });
  }

  ngOnInit(): void {
    this.loadingList.set(true);
    this.svc
      .listQuotes()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (data) => {
          this.quotes.set(data);
          this.loadingList.set(false);
        },
        error: (e: Error) => {
          this.listError.set(e.message);
          this.loadingList.set(false);
        },
      });
  }

  select(id: number): void {
    this.selectedId.set(id);
    this.select$.next(id);
  }
}
```

### quotes-list.component.html

```html
<div style="display:flex;gap:1.5rem;padding:1rem;font-family:sans-serif">

  <aside style="width:300px;flex-shrink:0;border-right:1px solid #e5e7eb;padding-right:1rem">
    <h2 style="font-size:1rem;font-weight:600;margin-bottom:0.75rem">Quotes</h2>

    @if (loadingList()) {
      <p style="color:#6b7280">Loading quotes…</p>
    } @else if (listError()) {
      <p style="color:#dc2626">Error: {{ listError() }}</p>
    } @else if (isEmpty()) {
      <p style="color:#6b7280">No quotes found.</p>
    } @else {
      @for (q of quotes(); track q.id) {
        <button (click)="select(q.id)"
          [style.background]="selectedId() === q.id ? '#eef2ff' : '#fff'"
          style="display:block;width:100%;text-align:left;padding:10px 12px;
                 margin-bottom:6px;border:1px solid;border-radius:8px;cursor:pointer">
          <span style="display:block;font-size:0.85rem;font-weight:600;color:#111827">
            {{ q.author }}
          </span>
          <span style="display:block;font-size:0.78rem;color:#6b7280;margin-top:2px">
            {{ q.text.slice(0, 60) }}…
          </span>
        </button>
      }
    }
  </aside>

  <main style="flex:1;padding-left:0.5rem">
    @if (loadingDetail()) {
      <p style="color:#6b7280">Loading…</p>
    } @else if (detailError()) {
      <p style="color:#dc2626">Error: {{ detailError() }}</p>
    } @else if (detail(); as q) {
      <blockquote style="border-left:4px solid #6366f1;padding-left:1rem;
                         font-size:1.15rem;line-height:1.7;margin:0 0 1rem 0">
        "{{ q.text }}"
      </blockquote>
      <p style="font-weight:600;color:#374151;margin:0">— {{ q.author }}</p>
      <p style="color:#9ca3af;font-size:0.8rem;margin-top:4px">
        {{ q.createdAt | date:'mediumDate' }}
      </p>
    } @else {
      <p style="color:#9ca3af;font-size:0.95rem">Select a quote to read it.</p>
    }
  </main>

</div>
```

---

## 3. Verification Log

### States and edges exercised

**Loading state**
Added `delay(2000)` temporarily to `listQuotes()` in `quotes-feature.service.ts`.
Refreshed the browser. The left panel showed `"Loading quotes…"` for 2 seconds before
the 10 quotes appeared. Confirmed `loadingList` signal drives the `@if (loadingList())` branch.

![Loading state](step3-loading-during.png)

---

**Error state**
Changed the URL in `listQuotes()` to `/api/quotes-broken` temporarily.
Refreshed the browser. The left panel showed `"Error: HTTP 404"`.
Zero uncaught JavaScript errors in the console — the error was fully handled by the
`error:` handler in `ngOnInit` which calls `this.listError.set(e.message)`.

![Error state](step4-error.png)

---

**Empty state**
Changed `size=10` to `size=0` in `listQuotes()` temporarily.
The API returned an empty array `[]`. The left panel showed `"No quotes found."`.
Confirmed `isEmpty` computed signal evaluates correctly:
`!loadingList() && !listError() && quotes().length === 0`.

![Empty state](step5-empty.png)

---

**Race condition (stale response guard)**
Added `delay(3000)` temporarily to `getQuote()`.
Clicked quote A (index 0), then 300ms later clicked quote B (index 1).
Waited 3.5 seconds. Only quote B's content appeared in the detail panel.
Quote A's response was cancelled by `switchMap` — the highlighted button stayed on B.
`switchMap` unsubscribes from the previous inner Observable the moment a new id arrives.

![Race guard](step6-race.png)

---

### List and detail loads

![List loads — 10 quotes](step1-list.png)

![Detail loads — text, author, date](step2-detail.png)

---

## 4. One Concrete Bug Caught and Fixed

**Bug: error handler placed at wrong level — kills the switchMap subscription permanently**

The agent's original code caught detail errors on the outer `.subscribe()`:

```ts
// WRONG — error on outer subscribe terminates the whole subscription
this.select$
  .pipe(
    switchMap((id) => {
      this.loadingDetail.set(true);
      return this.svc.getQuote(id);   // if this errors...
    }),
    takeUntilDestroyed(this.destroyRef)
  )
  .subscribe({
    next: (q) => { ... },
    error: (e: Error) => {             // ...this fires and kills the Observable permanently
      this.detailError.set(e.message);
      this.loadingDetail.set(false);
    },
  });
```

When `GET /api/quotes/:id` returns a 404 or 500, the error propagates through `switchMap`
to the outer Observable and terminates it permanently. The error message shows once — but
after that, every click calls `select$.next(id)` with no subscriber. All subsequent quote
clicks silently do nothing. The subscription is dead.

**Fix — catch the error inside `switchMap` so the outer Observable never errors:**

```ts
// CORRECT — error caught per-request inside switchMap, outer Observable stays alive
import { EMPTY } from 'rxjs';
import { catchError } from 'rxjs/operators';

this.select$
  .pipe(
    switchMap((id) => {
      this.loadingDetail.set(true);
      this.detailError.set(null);
      this.detail.set(null);
      return this.svc.getQuote(id).pipe(
        catchError((e: Error) => {
          this.detailError.set(e.message);
          this.loadingDetail.set(false);
          return EMPTY;   // complete this inner observable cleanly, no error propagated
        })
      );
    }),
    takeUntilDestroyed(this.destroyRef)
  )
  .subscribe({
    next: (q) => { this.detail.set(q); this.loadingDetail.set(false); },
    // no error: handler needed — errors are caught inside switchMap
  });
```

`EMPTY` completes the inner Observable without emitting a value and without error,
so `switchMap` moves on cleanly. The subscription stays alive for all future clicks.

---

## 5. What Breaks if the Week-1 API Contract Changes

| API Change | What breaks | How it fails |
|------------|-------------|--------------|
| `text` renamed to `content` | `q.text` in template returns `undefined` | List preview goes blank, detail blockquote goes blank — no error, silent failure |
| `createdAt` renamed to `created_at` | `q.createdAt \| date:'mediumDate'` gets `undefined` | Date disappears from detail panel — `DatePipe` renders nothing |
| Flat array `QuoteListItem[]` wrapped as `{ data: [], total: 0 }` | `quotes.set(data)` stores an object | `@for` iterates nothing; `isEmpty()` stays `false` — blank left panel with no message |
| `/api/quotes/:id` path changed to `/api/quotes/detail/:id` | `getQuote(id)` hits wrong URL | 404 response → `handleError` → `detailError` shows `"HTTP 404"`. Degrades gracefully. |
| Error shape `{ error: string }` changed to `{ message: string }` | `err.error?.error` returns `undefined` | Falls back to `"HTTP ${err.status}"` — less informative but no crash |
| `id` field renamed to `quoteId` | `track q.id` tracks `undefined` | Angular loses DOM identity on every re-render — all cards destroyed and recreated each fetch |
