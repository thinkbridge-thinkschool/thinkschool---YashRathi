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
