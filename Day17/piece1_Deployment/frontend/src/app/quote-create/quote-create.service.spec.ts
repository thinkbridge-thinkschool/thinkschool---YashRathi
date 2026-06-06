import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { QuoteCreateService } from './quote-create.service';
import { errorInterceptor } from '../error.interceptor';
import { isAppError } from '../app-error';
import type { AppError } from '../app-error';

// Integration: QuoteCreateService + errorInterceptor wired together.
// Proves AppError (not HttpErrorResponse) propagates and carries the right friendlyMessage.
describe('QuoteCreateService — AppError propagation', () => {
  let service: QuoteCreateService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(QuoteCreateService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('2xx success passes through without modification', () => {
    const mockResponse = { id: 1, author: 'A', text: 'T', createdAt: '2025-01-01' };
    let result: unknown;

    service.createQuote({ author: 'A', text: 'T' }).subscribe({ next: r => (result = r) });
    httpMock.expectOne('/api/quotes').flush(mockResponse);

    expect(result).toEqual(mockResponse);
  });

  // The four required status-to-message mappings
  const CASES: [number, string][] = [
    [401, 'You are not logged in. Please sign in.'],
    [403, 'You do not have permission to do this.'],
    [422, 'Please check your input and try again.'],
    [500, 'Something went wrong on our end. Please try again later.'],
  ];

  for (const [status, expectedMessage] of CASES) {
    it(`POST ${status} → AppError.friendlyMessage = "${expectedMessage}"`, () => {
      let error: unknown;
      service
        .createQuote({ author: 'A', text: 'T' })
        .subscribe({ error: e => (error = e) });

      const pd = { title: 'Error', status, detail: 'some detail' };
      httpMock.expectOne('/api/quotes').flush(pd, { status, statusText: 'Error' });

      // Must be AppError, not HttpErrorResponse
      expect(isAppError(error)).toBe(true);
      expect((error as AppError).friendlyMessage).toBe(expectedMessage);
      expect((error as AppError).status).toBe(status);
    });
  }

  it('propagates AppError.detail from ProblemDetails body', () => {
    let error: unknown;
    service.createQuote({ author: 'A', text: 'T' }).subscribe({ error: e => (error = e) });

    const pd = { title: 'Forbidden', status: 403, detail: 'You do not own this resource.' };
    httpMock.expectOne('/api/quotes').flush(pd, { status: 403, statusText: 'Forbidden' });

    expect((error as AppError).detail).toBe('You do not own this resource.');
    expect((error as AppError).raw).toEqual(pd);
  });

  it('422 ValidationProblemDetails — errors map preserved in raw.errors', () => {
    let error: unknown;
    service.createQuote({ author: '', text: '' }).subscribe({ error: e => (error = e) });

    const vpd = {
      title: 'Validation Failed',
      status: 422,
      detail: 'See errors.',
      errors: { author: ['Required.'], text: ['Too short.'] },
    };
    httpMock.expectOne('/api/quotes').flush(vpd, { status: 422, statusText: 'Unprocessable Entity' });

    expect((error as AppError).friendlyMessage).toBe('Please check your input and try again.');
    expect((error as AppError).raw.errors?.['author']).toContain('Required.');
  });
});
