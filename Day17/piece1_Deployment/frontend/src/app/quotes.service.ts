import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Quote } from './quote.model';

@Injectable({ providedIn: 'root' })
export class QuotesService {
  private http = inject(HttpClient);

  getQuotes(page: number, size: number, author?: string, text?: string): Observable<Quote[]> {
    let url = `/api/quotes?page=${page}&size=${size}`;
    if (author?.trim()) url += `&author=${encodeURIComponent(author.trim())}`;
    if (text?.trim())   url += `&text=${encodeURIComponent(text.trim())}`;
    return this.http.get<Quote[]>(url);
  }
}
