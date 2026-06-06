import { TestBed } from '@angular/core/testing';
import { provideHttpClient, HttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal, WritableSignal } from '@angular/core';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';

describe('authInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let tokenSignal: WritableSignal<string | null>;

  beforeEach(() => {
    // Fresh signal per test so tests don't share state
    tokenSignal = signal<string | null>(null);

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        // Provide a minimal mock — interceptor only calls .token()
        { provide: AuthService, useValue: { token: tokenSignal } },
      ],
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('adds Authorization: Bearer <token> when a token is present', () => {
    tokenSignal.set('abc-123');

    http.get('/api/quotes').subscribe();

    const req = httpMock.expectOne('/api/quotes');
    expect(req.request.headers.get('Authorization')).toBe('Bearer abc-123');
    req.flush([]);
  });

  it('does not add Authorization header when token is null', () => {
    tokenSignal.set(null);

    http.get('/api/quotes').subscribe();

    const req = httpMock.expectOne('/api/quotes');
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush([]);
  });

  it('reads the token live from the signal — picks up a changed token on the next request', () => {
    tokenSignal.set('token-v1');
    http.get('/api/a').subscribe();
    const req1 = httpMock.expectOne('/api/a');
    expect(req1.request.headers.get('Authorization')).toBe('Bearer token-v1');
    req1.flush([]);

    tokenSignal.set('token-v2');
    http.get('/api/b').subscribe();
    const req2 = httpMock.expectOne('/api/b');
    expect(req2.request.headers.get('Authorization')).toBe('Bearer token-v2');
    req2.flush([]);
  });

  it('sends request unchanged (no header mutation) — original request object is not mutated', () => {
    tokenSignal.set('secret');

    http.get('/api/quotes').subscribe();

    const req = httpMock.expectOne('/api/quotes');
    // Clone was used internally — the interceptor must not mutate original
    expect(req.request.headers.get('Authorization')).toBe('Bearer secret');
    req.flush([]);
  });
});
