import {
  Component,
  OnInit,
  inject,
  signal,
  DestroyRef,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { EMPTY, Subject, switchMap } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { QuotesFeatureService } from './quotes-feature.service';
import { QuoteDetail } from './quotes.types';
import { isAppError } from '../app-error';
import { QuotesStore } from '../stores/quotes.store';

@Component({
  selector: 'app-quotes-list',
  standalone: true,
  imports: [DatePipe],
  templateUrl: './quotes-list.component.html',
})
export class QuotesListComponent implements OnInit {
  private svc = inject(QuotesFeatureService);
  private destroyRef = inject(DestroyRef);
  store = inject(QuotesStore);

  selectedId    = signal<number | null>(null);
  detail        = signal<QuoteDetail | null>(null);
  loadingDetail = signal(false);
  detailError   = signal<string | null>(null);

  private select$ = new Subject<number>();

  constructor() {
    this.select$
      .pipe(
        switchMap((id) => {
          this.loadingDetail.set(true);
          this.detailError.set(null);
          this.detail.set(null);
          return this.svc.getQuote(id).pipe(
            catchError((e: unknown) => {
              this.detailError.set(
                isAppError(e) ? e.friendlyMessage :
                e instanceof Error ? e.message : 'Failed to load detail'
              );
              this.loadingDetail.set(false);
              return EMPTY;
            })
          );
        }),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: (q) => {
          this.detail.set(q);
          this.loadingDetail.set(false);
        },
      });
  }

  ngOnInit(): void {
    this.store.loadQuotes();
  }

  select(id: number): void {
    this.selectedId.set(id);
    this.select$.next(id);
  }
}
