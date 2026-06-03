import {
  Component,
  OnInit,
  inject,
  signal,
  computed,
  DestroyRef,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { EMPTY, Subject, switchMap } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { QuotesFeatureService } from './quotes-feature.service';
import { QuoteListItem, QuoteDetail } from './quotes.types';

@Component({
  selector: 'app-quotes-list',
  standalone: true,
  imports: [DatePipe],
  templateUrl: './quotes-list.component.html',
})
export class QuotesListComponent implements OnInit {
  private svc = inject(QuotesFeatureService);
  private destroyRef = inject(DestroyRef);

  quotes        = signal<QuoteListItem[]>([]);
  selectedId    = signal<number | null>(null);
  detail        = signal<QuoteDetail | null>(null);
  loadingList   = signal(false);
  loadingDetail = signal(false);
  listError     = signal<string | null>(null);
  detailError   = signal<string | null>(null);

  isEmpty = computed(
    () => !this.loadingList() && !this.listError() && this.quotes().length === 0
  );

  private select$ = new Subject<number>();

  constructor() {
    this.select$
      .pipe(
        switchMap((id) => {
          this.loadingDetail.set(true);
          this.detailError.set(null);
          this.detail.set(null);
          return this.svc.getQuote(id).pipe(
            catchError((e: Error) => {
              this.detailError.set(e.message);
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
    this.loadingList.set(true);
    this.svc
      .listQuotes()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (data) => {
          this.quotes.set(data);
          this.loadingList.set(false);
        },
        error: (e: Error) => {
          this.listError.set(e.message);
          this.loadingList.set(false);
        },
      });
  }

  select(id: number): void {
    this.selectedId.set(id);
    this.select$.next(id);
  }
}
