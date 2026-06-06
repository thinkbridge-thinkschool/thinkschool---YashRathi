/*
 * WHEN TO MOVE FROM SIGNALS TO NGRX:
 *
 * This signal store is the right tool as long as:
 *   - State is owned by ONE feature (quotes).
 *   - Actions are simple: load / add / delete; no branching async chains.
 *   - Team is small (1–3 devs) and everyone fits in one mental model.
 *   - No time-travel debugging or Redux DevTools required.
 *
 * Reach for @ngrx/signals (SignalStore) or full @ngrx/store when ANY of
 * these thresholds are crossed:
 *
 *   1. Cross-feature shared state — e.g. both QuotesListComponent AND a
 *      NotificationsComponent react to the same delete event. Signal
 *      services can be injected anywhere, but without Effects the
 *      coordination logic leaks into components.
 *
 *   2. Complex async chains — e.g. delete → re-fetch → invalidate cache →
 *      update sidebar count. More than two sequential HTTP calls per action
 *      means NgRx Effects start paying for themselves in clarity.
 *
 *   3. Time-travel / audit logging — legal/compliance replay, undo/redo,
 *      or a Redux DevTools debugging requirement. NgRx is irreplaceable here.
 *
 *   4. Team size ≥ 5 active contributors — the strict action/reducer contract
 *      prevents accidental direct writes; worth the boilerplate once enough
 *      hands touch the same state.
 *
 *   5. Optimistic updates with rollback — e.g. "delete locally, revert if
 *      the API returns 409". NgRx Effects make the rollback explicit and
 *      testable; in a signal service the error handler quietly becomes a
 *      mini-reducer anyway.
 *
 * Concrete rule: keep signals until:
 *   (cross-feature sharing) OR (>2 chained async steps per action)
 *   OR (team size ≥ 5)
 * Any one condition is enough to justify the migration.
 */

import { computed, inject, Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { EMPTY } from 'rxjs';
import { catchError, finalize, switchMap, tap } from 'rxjs/operators';
import { Quote } from '../quote.model';
import { isAppError } from '../app-error';

@Injectable({ providedIn: 'root' })
export class QuotesStore {
  private readonly http = inject(HttpClient);

  // --- private writeable signals ---

  private readonly _quotes = signal<Quote[]>([]);
  private readonly _selectedQuote = signal<Quote | null>(null);
  private readonly _isLoading = signal(false);
  private readonly _error = signal<string | null>(null);
  private readonly _currentPage = signal(1);
  private readonly _pageSize = signal(10);

  // --- public readonly projections ---

  readonly quotes = this._quotes.asReadonly();
  readonly selectedQuote = this._selectedQuote.asReadonly();
  readonly isLoading = this._isLoading.asReadonly();
  readonly error = this._error.asReadonly();
  readonly currentPage = this._currentPage.asReadonly();
  readonly pageSize = this._pageSize.asReadonly();

  // --- derived state ---

  readonly totalCount = computed(() => this._quotes().length);
  readonly hasError = computed(() => this._error() !== null);
  readonly isEmpty = computed(() => !this._isLoading() && this._quotes().length === 0);

  // --- actions ---

  loadQuotes(): void {
    this._isLoading.set(true);
    this._error.set(null);
    this.http
      .get<Quote[]>(`/api/quotes?page=${this._currentPage()}&size=${this._pageSize()}`)
      .pipe(
        tap(quotes => this._quotes.set(quotes)),
        catchError(err => {
          this._error.set(this.extractError(err));
          return EMPTY;
        }),
        finalize(() => this._isLoading.set(false)),
      )
      .subscribe();
  }

  loadQuote(id: number): void {
    this._isLoading.set(true);
    this._error.set(null);
    this.http
      .get<Quote>(`/api/quotes/${id}`)
      .pipe(
        tap(quote => this._selectedQuote.set(quote)),
        catchError(err => {
          this._error.set(this.extractError(err));
          return EMPTY;
        }),
        finalize(() => this._isLoading.set(false)),
      )
      .subscribe();
  }

  // POST then immediately re-fetches the list in one chain so finalize
  // fires only after both requests settle — avoids an isLoading=false flash
  // between the POST completing and the GET starting.
  addQuote(author: string, text: string): void {
    this._isLoading.set(true);
    this._error.set(null);
    this.http
      .post<Quote>('/api/quotes', { author, text })
      .pipe(
        switchMap(() =>
          this.http.get<Quote[]>(`/api/quotes?page=${this._currentPage()}&size=${this._pageSize()}`)
        ),
        tap(quotes => this._quotes.set(quotes)),
        catchError(err => {
          this._error.set(this.extractError(err));
          return EMPTY;
        }),
        finalize(() => this._isLoading.set(false)),
      )
      .subscribe();
  }

  // Confirmed removal: quote is filtered out of the signal only after the
  // server returns 204. The UI does not update until the DELETE succeeds.
  deleteQuote(id: number): void {
    this._isLoading.set(true);
    this._error.set(null);
    this.http
      .delete<void>(`/api/quotes/${id}`)
      .pipe(
        tap(() => this._quotes.update(qs => qs.filter(q => q.id !== id))),
        catchError(err => {
          this._error.set(this.extractError(err));
          return EMPTY;
        }),
        finalize(() => this._isLoading.set(false)),
      )
      .subscribe();
  }

  setPage(page: number): void {
    this._currentPage.set(page);
  }

  clearError(): void {
    this._error.set(null);
  }

  // --- private helpers ---

  private extractError(err: unknown): string {
    if (isAppError(err)) return err.friendlyMessage;
    if (err instanceof Error) return err.message;
    return 'An unexpected error occurred.';
  }
}
