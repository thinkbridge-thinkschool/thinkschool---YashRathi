import { TestBed } from '@angular/core/testing';
import { provideHttpClient, HttpClient, HttpErrorResponse, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { retryInterceptor } from './retry.interceptor';

// Backoff schedule defined in the interceptor: 1s, 2s, 4s
const BACKOFF = [1000, 2000, 4000];

describe('retryInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    // Fake timers prevent real 1s/2s/4s waits in tests
    vi.useFakeTimers();

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([retryInterceptor])),
        provideHttpClientTesting(),
      ],
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    vi.useRealTimers();
    httpMock.verify(); // fails test if any unhandled request remains
  });

  // ─── Non-GET methods are never retried ──────────────────────────────────────

  it('does not retry POST on 5xx — exactly 1 request made', () => {
    let caughtError: unknown;
    http.post('/api/quotes', { text: 'x', author: 'y' })
      .subscribe({ error: e => (caughtError = e) });

    httpMock.expectOne('/api/quotes').flush({}, { status: 500, statusText: 'Server Error' });

    expect(caughtError).toBeInstanceOf(HttpErrorResponse);
    expect((caughtError as HttpErrorResponse).status).toBe(500);
    // httpMock.verify() in afterEach confirms no second request was made
  });

  it('does not retry PUT on 5xx — exactly 1 request made', () => {
    let caughtError: unknown;
    http.put('/api/quotes/1', {}).subscribe({ error: e => (caughtError = e) });

    httpMock.expectOne('/api/quotes/1').flush({}, { status: 500, statusText: 'Server Error' });

    expect(caughtError).toBeInstanceOf(HttpErrorResponse);
  });

  it('does not retry DELETE on 5xx — exactly 1 request made', () => {
    let caughtError: unknown;
    http.delete('/api/quotes/1').subscribe({ error: e => (caughtError = e) });

    httpMock.expectOne('/api/quotes/1').flush({}, { status: 500, statusText: 'Server Error' });

    expect(caughtError).toBeInstanceOf(HttpErrorResponse);
  });

  // ─── GET + 4xx: never retried ────────────────────────────────────────────────

  it('does not retry GET on 401 — propagates immediately, 1 request only', () => {
    let caughtError: unknown;
    http.get('/api/quotes').subscribe({ error: e => (caughtError = e) });

    httpMock.expectOne('/api/quotes').flush({}, { status: 401, statusText: 'Unauthorized' });

    expect(caughtError).toBeInstanceOf(HttpErrorResponse);
    expect((caughtError as HttpErrorResponse).status).toBe(401);
  });

  it('does not retry GET on 422 — propagates immediately, 1 request only', () => {
    let caughtError: unknown;
    http.get('/api/quotes').subscribe({ error: e => (caughtError = e) });

    httpMock.expectOne('/api/quotes').flush({}, { status: 422, statusText: 'Unprocessable Entity' });

    expect(caughtError).toBeInstanceOf(HttpErrorResponse);
    expect((caughtError as HttpErrorResponse).status).toBe(422);
  });

  // ─── GET + 5xx: retried with exponential backoff ────────────────────────────

  it('retries GET on 500, succeeds on the 2nd attempt (1st retry)', async () => {
    const mockData = [{ id: 1, author: 'A', text: 'T', createdAt: '2025-01-01' }];
    let result: unknown;

    http.get('/api/quotes').subscribe({ next: r => (result = r) });

    // Original attempt fails
    httpMock.expectOne('/api/quotes').flush({}, { status: 500, statusText: 'Server Error' });

    // Advance past 1st backoff (1s) — 1st retry fires
    await vi.advanceTimersByTimeAsync(BACKOFF[0]);

    // 1st retry succeeds
    httpMock.expectOne('/api/quotes').flush(mockData);

    expect(result).toEqual(mockData);
  });

  it('retries GET on 5xx exactly 3 times (4 total requests) then propagates the error', async () => {
    let caughtError: unknown;

    http.get('/api/quotes').subscribe({ error: e => (caughtError = e) });

    // Attempt 1 of 4
    httpMock.expectOne('/api/quotes').flush({}, { status: 503, statusText: 'Service Unavailable' });
    await vi.advanceTimersByTimeAsync(BACKOFF[0]); // wait 1s → retry 1

    // Attempt 2 of 4 (retry 1)
    httpMock.expectOne('/api/quotes').flush({}, { status: 503, statusText: 'Service Unavailable' });
    await vi.advanceTimersByTimeAsync(BACKOFF[1]); // wait 2s → retry 2

    // Attempt 3 of 4 (retry 2)
    httpMock.expectOne('/api/quotes').flush({}, { status: 503, statusText: 'Service Unavailable' });
    await vi.advanceTimersByTimeAsync(BACKOFF[2]); // wait 4s → retry 3

    // Attempt 4 of 4 (retry 3 — final) — error propagates immediately, no more timer
    httpMock.expectOne('/api/quotes').flush({}, { status: 503, statusText: 'Service Unavailable' });

    expect(caughtError).toBeInstanceOf(HttpErrorResponse);
    expect((caughtError as HttpErrorResponse).status).toBe(503);
  });

  it('does not make a 5th request after 3 retries are exhausted', async () => {
    http.get('/api/quotes').subscribe({ error: () => {} });

    httpMock.expectOne('/api/quotes').flush({}, { status: 500, statusText: 'Server Error' });
    await vi.advanceTimersByTimeAsync(BACKOFF[0]);

    httpMock.expectOne('/api/quotes').flush({}, { status: 500, statusText: 'Server Error' });
    await vi.advanceTimersByTimeAsync(BACKOFF[1]);

    httpMock.expectOne('/api/quotes').flush({}, { status: 500, statusText: 'Server Error' });
    await vi.advanceTimersByTimeAsync(BACKOFF[2]);

    httpMock.expectOne('/api/quotes').flush({}, { status: 500, statusText: 'Server Error' });

    // Advance past any possible further delay — should be no 5th request
    await vi.advanceTimersByTimeAsync(10_000);
    httpMock.expectNone('/api/quotes'); // fails if a 5th request was made
  });
});
