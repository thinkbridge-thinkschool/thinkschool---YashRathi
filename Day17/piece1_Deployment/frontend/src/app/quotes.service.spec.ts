import { TestBed } from '@angular/core/testing';
import { provideHttpClient, HttpErrorResponse } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { QuotesService } from './quotes.service';
import type { Quote } from './quote.model';

// Characterization test: pins the current behaviour of QuotesService
// before any interceptor work begins.
// Run with: cd quotes-ui && ng test

describe('QuotesService — characterization', () => {
  let service: QuotesService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        // No interceptors — isolate the service under test
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(QuotesService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    // Fails the test if any request was made that we did not assert
    httpMock.verify();
  });

  // ─── Test 1: happy path ────────────────────────────────────────────────────

  describe('getQuotes(page, size)', () => {
    it('sends GET /api/quotes?page=1&size=10 and maps the response to Quote[]', () => {
      // Exact shape from src/app/quote.model.ts
      // id: number  (not string)
      // createdAt: string (extra field the prompt did not mention)
      const mockQuotes: Quote[] = [
        {
          id: 1,
          author: 'Marcus Aurelius',
          text: 'You have power over your mind, not outside events.',
          createdAt: '2025-01-01T00:00:00Z',
        },
        {
          id: 2,
          author: 'Epictetus',
          text: 'Make the best use of what is in your power.',
          createdAt: '2025-01-02T00:00:00Z',
        },
      ];

      let result: Quote[] | undefined;
      service
        .getQuotes(1, 10)
        .subscribe({ next: (q) => (result = q) });

      // URL is built as a template string, not via HttpParams,
      // so expectOne matches on the full urlWithParams string.
      const req = httpMock.expectOne('/api/quotes?page=1&size=10');

      expect(req.request.method).toBe('GET');

      req.flush(mockQuotes);

      // Verify the whole array shape
      expect(result).toHaveLength(2);

      // Verify exact field names and types from the Quote interface
      expect(result![0].id).toBe(1);            // number, not string
      expect(result![0].author).toBe('Marcus Aurelius');
      expect(result![0].text).toBe('You have power over your mind, not outside events.');
      expect(result![0].createdAt).toBe('2025-01-01T00:00:00Z');

      expect(result![1].id).toBe(2);
      expect(result![1].author).toBe('Epictetus');
    });

    it('encodes page and size params correctly for non-default values', () => {
      service.getQuotes(3, 25).subscribe();
      const req = httpMock.expectOne('/api/quotes?page=3&size=25');
      expect(req.request.method).toBe('GET');
      req.flush([]);
    });

    it('appends &author= when the author filter is provided', () => {
      service.getQuotes(1, 10, 'Aurelius').subscribe();
      const req = httpMock.expectOne('/api/quotes?page=1&size=10&author=Aurelius');
      req.flush([]);
    });

    it('appends &text= when the text filter is provided', () => {
      service.getQuotes(1, 10, undefined, 'power').subscribe();
      const req = httpMock.expectOne('/api/quotes?page=1&size=10&text=power');
      req.flush([]);
    });
  });

  // ─── Test 2: 4xx error propagation ────────────────────────────────────────
  // QuotesService.getQuotes() has NO catchError — there is no transformation.
  // The raw HttpErrorResponse propagates to the subscriber.
  // err.error contains the parsed response body (the ProblemDetails object).

  describe('getQuotes — 4xx error propagation', () => {
    it('propagates a 401 response as HttpErrorResponse; err.error holds the ProblemDetails body', () => {
      const problemDetails = {
        title: 'Unauthorized',
        status: 401,
        detail: 'Authentication is required to access this resource.',
      };

      let caughtError: HttpErrorResponse | undefined;
      service.getQuotes(1, 10).subscribe({
        next: () => {
          throw new Error('Expected the observable to error, but it succeeded.');
        },
        error: (err: HttpErrorResponse) => (caughtError = err),
      });

      const req = httpMock.expectOne('/api/quotes?page=1&size=10');
      req.flush(problemDetails, { status: 401, statusText: 'Unauthorized' });

      // The raw HttpErrorResponse (no catchError in service)
      expect(caughtError).toBeInstanceOf(HttpErrorResponse);
      expect(caughtError!.status).toBe(401);

      // err.error is the parsed response body — the ProblemDetails object
      expect(caughtError!.error).toEqual(problemDetails);
      expect(caughtError!.error.title).toBe('Unauthorized');
      expect(caughtError!.error.status).toBe(401);
      expect(caughtError!.error.detail).toBe('Authentication is required to access this resource.');
    });

    it('propagates a 422 ValidationProblemDetails response with errors map', () => {
      const validationProblemDetails = {
        title: 'One or more validation errors occurred.',
        status: 422,
        detail: 'See the errors property for details.',
        errors: {
          author: ['Author is required.'],
          text: ['Text must be at least 5 characters.'],
        },
      };

      let caughtError: HttpErrorResponse | undefined;
      service.getQuotes(1, 10).subscribe({
        next: () => {
          throw new Error('Expected an error response.');
        },
        error: (err: HttpErrorResponse) => (caughtError = err),
      });

      const req = httpMock.expectOne('/api/quotes?page=1&size=10');
      req.flush(validationProblemDetails, {
        status: 422,
        statusText: 'Unprocessable Entity',
      });

      expect(caughtError).toBeInstanceOf(HttpErrorResponse);
      expect(caughtError!.status).toBe(422);
      expect(caughtError!.error.title).toBe('One or more validation errors occurred.');
      expect(caughtError!.error.errors['author']).toContain('Author is required.');
      expect(caughtError!.error.errors['text']).toContain(
        'Text must be at least 5 characters.',
      );
    });
  });
});
