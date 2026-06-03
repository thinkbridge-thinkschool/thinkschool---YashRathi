import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Observable, throwError } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { CreateQuotePayload, CreateQuoteResponse } from './quote-create.types';

@Injectable({ providedIn: 'root' })
export class QuoteCreateService {
  private http = inject(HttpClient);

  createQuote(payload: CreateQuotePayload): Observable<CreateQuoteResponse> {
    return this.http
      .post<CreateQuoteResponse>('/api/quotes', payload)
      .pipe(catchError(this.handleError));
  }

  private handleError(err: HttpErrorResponse): Observable<never> {
    const message: string =
      (err.error as { error?: string } | null)?.error ?? `HTTP ${err.status}`;
    return throwError(() => new Error(message));
  }
}
