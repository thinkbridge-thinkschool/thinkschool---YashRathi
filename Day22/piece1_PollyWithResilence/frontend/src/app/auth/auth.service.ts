import { inject, Injectable, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, throwError } from 'rxjs';
import { catchError, tap } from 'rxjs/operators';
import { LoginPayload, LoginResponse } from './auth.types';
import { isAppError } from '../app-error';

const TOKEN_KEY = 'access_token';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private http = inject(HttpClient);

  token = signal<string | null>(localStorage.getItem(TOKEN_KEY));
  isLoggedIn = computed(() => this.token() !== null);

  login(payload: LoginPayload): Observable<LoginResponse> {
    return this.http
      .post<LoginResponse>('/api/auth/login', payload)
      .pipe(
        tap(res => {
          localStorage.setItem(TOKEN_KEY, res.accessToken);
          this.token.set(res.accessToken);
        }),
        catchError(err => this.handleLoginError(err)),
      );
  }

  logout(): void {
    localStorage.removeItem(TOKEN_KEY);
    this.token.set(null);
  }

  // Login 401 means wrong credentials, not "not logged in".
  // Override the generic friendlyMessage for this specific context.
  private handleLoginError(err: unknown): Observable<never> {
    if (isAppError(err)) {
      const message = err.status === 401
        ? 'Invalid email or password.'
        : err.friendlyMessage;
      return throwError(() => new Error(message));
    }
    return throwError(() => new Error('An unexpected error occurred.'));
  }
}
