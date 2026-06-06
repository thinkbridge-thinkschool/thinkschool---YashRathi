import { TestBed } from '@angular/core/testing';
import { provideHttpClient, HttpClient, HttpErrorResponse, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { errorInterceptor } from './error.interceptor';
import type { AppError } from './app-error';

describe('errorInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  // ─── 2xx passes through untouched ──────────────────────────────────────────

  it('passes 2xx responses through without modification', () => {
    const data = [{ id: 1, author: 'A', text: 'T', createdAt: '2025-01-01' }];
    let result: unknown;

    http.get('/api/quotes').subscribe({ next: r => (result = r) });
    httpMock.expectOne('/api/quotes').flush(data);

    expect(result).toEqual(data);
  });

  // ─── AppError shape ─────────────────────────────────────────────────────────

  it('transforms HttpErrorResponse into AppError — not HttpErrorResponse anymore', () => {
    const pd = { title: 'Unauthorized', status: 401, detail: 'No token provided.' };
    let error: unknown;

    http.get('/api/quotes').subscribe({ error: e => (error = e) });
    httpMock.expectOne('/api/quotes').flush(pd, { status: 401, statusText: 'Unauthorized' });

    // Must NOT be a raw HttpErrorResponse
    expect(error).not.toBeInstanceOf(HttpErrorResponse);

    const appError = error as AppError;
    expect(appError.friendlyMessage).toBe('You are not logged in. Please sign in.');
    expect(appError.status).toBe(401);
    expect(appError.detail).toBe('No token provided.');
    expect(appError.raw).toEqual(pd);
  });

  // ─── friendlyMessage mapping ────────────────────────────────────────────────

  const STATUS_MESSAGES: [number, string][] = [
    [401, 'You are not logged in. Please sign in.'],
    [403, 'You do not have permission to do this.'],
    [422, 'Please check your input and try again.'],
    [500, 'Something went wrong on our end. Please try again later.'],
    [404, 'An unexpected error occurred.'],   // default
    [502, 'An unexpected error occurred.'],   // default
    [503, 'An unexpected error occurred.'],   // default
  ];

  for (const [status, expectedMessage] of STATUS_MESSAGES) {
    it(`status ${status} → friendlyMessage: "${expectedMessage}"`, () => {
      const pd = { title: 'Error', status, detail: 'some detail' };
      let error: unknown;

      http.get('/api/quotes').subscribe({ error: e => (error = e) });
      httpMock.expectOne('/api/quotes').flush(pd, { status, statusText: 'Error' });

      expect((error as AppError).friendlyMessage).toBe(expectedMessage);
      expect((error as AppError).status).toBe(status);
    });
  }

  // ─── ProblemDetails fields preserved in raw ─────────────────────────────────

  it('preserves detail field from ProblemDetails in AppError.detail', () => {
    const pd = { title: 'Forbidden', status: 403, detail: 'Owner access only.' };
    let error: unknown;

    http.get('/api/quotes').subscribe({ error: e => (error = e) });
    httpMock.expectOne('/api/quotes').flush(pd, { status: 403, statusText: 'Forbidden' });

    expect((error as AppError).detail).toBe('Owner access only.');
    expect((error as AppError).raw).toEqual(pd);
  });

  it('preserves errors map from ValidationProblemDetails in raw.errors', () => {
    const vpd = {
      title: 'Validation Failed',
      status: 422,
      detail: 'See errors property.',
      errors: {
        author: ['Author is required.'],
        text: ['Text must be at least 5 characters.'],
      },
    };
    let error: unknown;

    http.get('/api/quotes').subscribe({ error: e => (error = e) });
    httpMock.expectOne('/api/quotes').flush(vpd, { status: 422, statusText: 'Unprocessable Entity' });

    const appError = error as AppError;
    expect(appError.raw.errors?.['author']).toContain('Author is required.');
    expect(appError.raw.errors?.['text']).toContain('Text must be at least 5 characters.');
  });

  // ─── Network error (status 0) ────────────────────────────────────────────────

  it('maps a network error (status 0) to AppError with fallback ProblemDetails', () => {
    let error: unknown;

    http.get('/api/quotes').subscribe({ error: e => (error = e) });
    httpMock.expectOne('/api/quotes').error(new ProgressEvent('error'));

    const appError = error as AppError;
    expect(appError.status).toBe(0);
    expect(appError.friendlyMessage).toBe('An unexpected error occurred.');
    expect(appError.raw).toMatchObject({ title: 'Unknown error', status: 0, detail: '' });
  });

  // ─── Response body without ProblemDetails shape ─────────────────────────────

  it('uses fallback raw when response body is not a ProblemDetails object', () => {
    // Some APIs return a plain string or unstructured body on errors
    let error: unknown;

    http.get('/api/quotes').subscribe({ error: e => (error = e) });
    // Flush with a body that has no `title` or `status` — not a ProblemDetails
    httpMock.expectOne('/api/quotes').flush('Internal Server Error', {
      status: 500,
      statusText: 'Server Error',
    });

    const appError = error as AppError;
    expect(appError.status).toBe(500);
    expect(appError.friendlyMessage).toBe('Something went wrong on our end. Please try again later.');
    expect(appError.raw).toMatchObject({ title: 'Unknown error', status: 500, detail: '' });
  });
});
