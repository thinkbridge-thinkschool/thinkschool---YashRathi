import { Component, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { QuotesService } from './quotes.service';
import { Quote } from './quote.model';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './app.component.html',
  styleUrl: './app.component.css'
})
export class AppComponent {
  private quotesService = inject(QuotesService);

  // --- Signals ---
  currentPage  = signal(1);
  pageSize     = signal(10);
  searchTerm   = signal('');   // kept for spec; mirrors authorSearch
  authorSearch = signal('');
  quoteSearch  = signal('');
  quotes       = signal<Quote[]>([]);
  isLoading    = signal(false);
  errorMessage = signal<string | null>(null);

  // --- Computed ---
  filteredQuotes = computed(() => this.quotes());
  totalCount     = computed(() => this.filteredQuotes().length);
  pageStart      = computed(() => (this.currentPage() - 1) * this.pageSize() + 1);

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
        this.isLoading.set(false);
      },
      error: (err) => {
        this.errorMessage.set(err.message ?? 'Failed to load quotes');
        this.isLoading.set(false);
      }
    });
  }

  onAuthorChange(value: string): void {
    this.authorSearch.set(value);
    this.searchTerm.set(value);
    this.currentPage.set(1);
  }

  onQuoteChange(value: string): void {
    this.quoteSearch.set(value);
    this.currentPage.set(1);
  }

  clearAll(): void {
    this.authorSearch.set('');
    this.quoteSearch.set('');
    this.searchTerm.set('');
    this.currentPage.set(1);
  }

  previousPage(): void {
    if (this.currentPage() > 1) this.currentPage.update(p => p - 1);
  }

  nextPage(): void {
    this.currentPage.update(p => p + 1);
  }
}
