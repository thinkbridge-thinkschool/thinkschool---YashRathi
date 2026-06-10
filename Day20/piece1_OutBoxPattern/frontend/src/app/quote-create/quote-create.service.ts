import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { CreateQuotePayload, CreateQuoteResponse } from './quote-create.types';

@Injectable({ providedIn: 'root' })
export class QuoteCreateService {
  private http = inject(HttpClient);

  createQuote(payload: CreateQuotePayload): Observable<CreateQuoteResponse> {
    return this.http.post<CreateQuoteResponse>('/api/quotes', payload);
  }
}
