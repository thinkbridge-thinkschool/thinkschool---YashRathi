import { Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { QuotesFeatureService } from '../quotes/quotes-feature.service';
import { QuoteDetail } from '../quotes/quotes.types';
import { isAppError } from '../app-error';

@Component({
  selector: 'app-quote-detail',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './quote-detail.component.html',
  styleUrl: './quote-detail.component.css'
})
export class QuoteDetailComponent implements OnInit {
  private route = inject(ActivatedRoute);
  private svc = inject(QuotesFeatureService);

  quote = signal<QuoteDetail | null>(null);
  isLoading = signal(true);
  errorMessage = signal<string | null>(null);

  ngOnInit(): void {
    const raw = this.route.snapshot.paramMap.get('id');
    const id = Number(raw);

    if (!raw || !Number.isFinite(id) || id <= 0) {
      this.isLoading.set(false);
      this.errorMessage.set('Invalid quote ID.');
      return;
    }

    this.svc.getQuote(id).subscribe({
      next: (q) => {
        this.quote.set(q);
        this.isLoading.set(false);
      },
      error: (err: unknown) => {
        this.isLoading.set(false);
        if (isAppError(err) && err.status === 404) {
          this.errorMessage.set('Quote not found.');
        } else if (isAppError(err)) {
          this.errorMessage.set(err.friendlyMessage);
        } else if (err instanceof Error) {
          this.errorMessage.set(err.message);
        } else {
          this.errorMessage.set('Failed to load quote.');
        }
      }
    });
  }
}
