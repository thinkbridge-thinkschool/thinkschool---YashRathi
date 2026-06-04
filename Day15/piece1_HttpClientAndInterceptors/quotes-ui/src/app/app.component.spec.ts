import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { AppComponent } from './app.component';
import { QuotesService } from './quotes.service';
import { AuthService } from './auth/auth.service';
import { errorInterceptor } from './error.interceptor';

// Minimal AuthService mock — keeps the @if(!auth.isLoggedIn()) gate open.
const mockAuth = {
  token:     signal<string | null>('fake-token'),
  isLoggedIn: { _computed: true } as unknown as ReturnType<typeof signal>,
  logout: () => {},
};
// Make isLoggedIn a computed-like signal that returns true
Object.defineProperty(mockAuth, 'isLoggedIn', { get: () => () => true });

describe('AppComponent — loadQuotes error path', () => {
  let httpMock: HttpTestingController;
  let component: AppComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [AppComponent],
      providers: [
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: mockAuth },
      ],
    });

    // Create the component but do NOT call detectChanges yet so the
    // constructor effect does not fire an uncontrolled HTTP request.
    const fixture = TestBed.createComponent(AppComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);

    // Flush any request the effect already triggered on construction
    httpMock.match(() => true).forEach(r =>
      r.flush([], { status: 200, statusText: 'OK' })
    );
  });

  afterEach(() => httpMock.verify());

  const ERROR_CASES: [number, string][] = [
    [401, 'You are not logged in. Please sign in.'],
    [403, 'You do not have permission to do this.'],
    [422, 'Please check your input and try again.'],
    [500, 'Something went wrong on our end. Please try again later.'],
  ];

  for (const [status, expectedMessage] of ERROR_CASES) {
    it(`loadQuotes ${status} → errorMessage() = "${expectedMessage}"`, () => {
      component.loadQuotes(1, 10);

      const pd = { title: 'Error', status, detail: 'detail' };
      httpMock.expectOne(r => r.url.includes('/api/quotes'))
              .flush(pd, { status, statusText: 'Error' });

      expect(component.errorMessage()).toBe(expectedMessage);
      expect(component.isLoading()).toBe(false);
    });
  }

  it('loadQuotes 200 → errorMessage() is null, quotes set', () => {
    const mockQuotes = [{ id: 1, author: 'A', text: 'T', createdAt: '2025-01-01' }];
    component.loadQuotes(1, 10);
    httpMock.expectOne(r => r.url.includes('/api/quotes')).flush(mockQuotes);

    expect(component.errorMessage()).toBeNull();
    expect(component.quotes()).toEqual(mockQuotes);
  });
});
