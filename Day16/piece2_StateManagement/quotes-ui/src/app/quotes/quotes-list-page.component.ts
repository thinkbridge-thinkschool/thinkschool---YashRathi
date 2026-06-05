import { Component, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink, Router } from '@angular/router';
import { QuotesService } from '../quotes.service';
import { Quote } from '../quote.model';
import { QuoteCreateComponent } from '../quote-create/quote-create.component';
import { AuthService } from '../auth/auth.service';
import { isAppError } from '../app-error';

@Component({
  selector: 'app-quotes-list-page',
  standalone: true,
  imports: [FormsModule, RouterLink, QuoteCreateComponent],
  templateUrl: './quotes-list-page.component.html',
  styleUrl: '../app.component.css'
})
export class QuotesListPageComponent {
  private quotesService = inject(QuotesService);
  private router = inject(Router);
  auth = inject(AuthService);

  logout(): void {
    this.auth.logout();
    this.router.navigate(['/login']);
  }

  currentPage  = signal(1);
  pageSize     = signal(10);
  authorSearch = signal('');
  quoteSearch  = signal('');
  quotes       = signal<Quote[]>([]);
  isLoading    = signal(false);
  errorMessage = signal<string | null>(null);
  hasMore      = signal(true);

  totalCount = computed(() => this.quotes().length);

  summary = computed(() => {
    const author = this.authorSearch().trim();
    const text   = this.quoteSearch().trim();
    const count  = this.totalCount();
    const page   = this.currentPage();
    if (author && text)  return `Found ${count} quotes by "${author}" containing "${text}" — Page ${page}`;
    if (author)          return `Found ${count} quotes by "${author}" — Page ${page}`;
    if (text)            return `Found ${count} quotes containing "${text}" — Page ${page}`;
    return `Showing ${count} quotes — Page ${page}`;
  });

  hasActiveFilter = computed(() =>
    this.authorSearch().trim().length > 0 || this.quoteSearch().trim().length > 0
  );

  viewState = computed<'loading' | 'error' | 'success'>(() => {
    if (this.isLoading()) return 'loading';
    if (this.errorMessage() !== null) return 'error';
    return 'success';
  });

  constructor() {
    effect(() => {
      const page   = this.currentPage();
      const size   = this.pageSize();
      const author = this.authorSearch();
      const text   = this.quoteSearch();
      this.loadQuotes(page, size, author, text);
    });
  }

  loadQuotes(page: number, size: number, author?: string, text?: string): void {
    this.isLoading.set(true);
    this.errorMessage.set(null);
    this.quotesService.getQuotes(page, size, author, text).subscribe({
      next: (data) => {
        this.quotes.set(data);
        this.hasMore.set(data.length >= size);
        this.isLoading.set(false);
      },
      error: (err: unknown) => {
        if (isAppError(err)) {
          this.errorMessage.set(err.friendlyMessage);
        } else if (err instanceof Error) {
          this.errorMessage.set(err.message);
        } else {
          this.errorMessage.set('Failed to load quotes');
        }
        this.isLoading.set(false);
      }
    });
  }

  onAuthorChange(value: string): void {
    this.authorSearch.set(value);
    this.currentPage.set(1);
  }

  onQuoteChange(value: string): void {
    this.quoteSearch.set(value);
    this.currentPage.set(1);
  }

  clearAll(): void {
    this.authorSearch.set('');
    this.quoteSearch.set('');
    this.currentPage.set(1);
  }

  previousPage(): void {
    if (this.currentPage() > 1) this.currentPage.update(p => p - 1);
  }

  nextPage(): void {
    this.currentPage.update(p => p + 1);
  }
}
