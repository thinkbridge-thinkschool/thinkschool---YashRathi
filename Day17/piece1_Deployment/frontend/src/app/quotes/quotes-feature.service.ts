import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { QuoteListItem, QuoteDetail } from './quotes.types';

@Injectable({ providedIn: 'root' })
export class QuotesFeatureService {
  private http = inject(HttpClient);

  listQuotes(page = 1, size = 10): Observable<QuoteListItem[]> {
    return this.http.get<QuoteListItem[]>(`/api/quotes?page=${page}&size=${size}`);
  }

  getQuote(id: number): Observable<QuoteDetail> {
    return this.http.get<QuoteDetail>(`/api/quotes/${id}`);
  }
}
