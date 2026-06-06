import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
import { throwError, timer } from 'rxjs';
import { retry } from 'rxjs/operators';

const MAX_RETRIES = 3;
const BACKOFF_MS = [1000, 2000, 4000] as const;

export const retryInterceptor: HttpInterceptorFn = (req, next) => {
  if (req.method !== 'GET') {
    return next(req);
  }

  return next(req).pipe(
    retry({
      count: MAX_RETRIES,
      delay: (error: unknown, retryCount: number) => {
        if (error instanceof HttpErrorResponse && error.status >= 400 && error.status < 500) {
          // 4xx client errors are not retryable — propagate immediately
          return throwError(() => error);
        }
        // 5xx or network error (status 0) — exponential backoff
        // retryCount is 1-based: 1→1s, 2→2s, 3→4s
        return timer(BACKOFF_MS[retryCount - 1] ?? BACKOFF_MS[BACKOFF_MS.length - 1]);
      },
    }),
  );
};
