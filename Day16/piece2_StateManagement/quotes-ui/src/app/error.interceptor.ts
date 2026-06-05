import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
import { throwError } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { AppError, ProblemDetails, isProblemDetails } from './app-error';

export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  return next(req).pipe(
    catchError((err: unknown) => {
      if (!(err instanceof HttpErrorResponse)) {
        // Not an HTTP error (e.g. a programming error) — pass through untouched
        return throwError(() => err);
      }

      const status = err.status;
      const raw: ProblemDetails = isProblemDetails(err.error)
        ? err.error
        : { title: 'Unknown error', status, detail: '' };

      const appError: AppError = {
        friendlyMessage: toFriendlyMessage(status),
        status,
        detail: raw.detail,
        raw,
      };

      return throwError(() => appError);
    }),
  );
};

function toFriendlyMessage(status: number): string {
  switch (status) {
    case 401: return 'You are not logged in. Please sign in.';
    case 403: return 'You do not have permission to do this.';
    case 422: return 'Please check your input and try again.';
    case 500: return 'Something went wrong on our end. Please try again later.';
    default:  return 'An unexpected error occurred.';
  }
}
