import { Component, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { QuotesService } from './quotes.service';
import { Quote } from './quote.model';
import { QuoteCreateComponent } from './quote-create/quote-create.component';
import { LoginComponent } from './auth/login.component';
import { AuthService } from './auth/auth.service';
import { isAppError } from './app-error';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [FormsModule, QuoteCreateComponent, LoginComponent],
  templateUrl: './app.component.html',
  styleUrl: './app.component.css'
})
export class AppComponent {
  private quotesService = inject(QuotesService);
  auth = inject(AuthService);

  logout(): void { this.auth.logout(); }

  selectedQuote = signal<Quote | null>(null);
  panelOpen     = signal(false);

  openQuote(quote: Quote): void {
    this.selectedQuote.set(quote);
    this.panelOpen.set(true);
  }

  closePanel(): void {
    this.panelOpen.set(false);
  }

  // --- Signals ---
  currentPage  = signal(1);
  pageSize     = signal(10);
  authorSearch = signal('');
  quoteSearch  = signal('');
  quotes       = signal<Quote[]>([]);
  isLoading    = signal(false);
  errorMessage = signal<string | null>(null);
  // False when the last fetch returned fewer items than pageSize (end of data).
  hasMore      = signal(true);

  // --- Computed ---
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
    // Effect fires when page, size, authorSearch OR quoteSearch changes
    effect(() => {
      const page   = this.currentPage();
      const size   = this.pageSize();
      const author = this.authorSearch();
      const text   = this.quoteSearch();
      console.log(`[effect] Fetching quotes — page=${page} size=${size} author="${author}" text="${text}"`);
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
