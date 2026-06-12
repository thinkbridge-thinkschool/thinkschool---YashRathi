import { ApplicationConfig, provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter, withViewTransitions, withComponentInputBinding } from '@angular/router';
import { routes } from './app.routes';
import { authInterceptor } from './auth/auth.interceptor';
import { errorInterceptor } from './error.interceptor';
import { retryInterceptor } from './retry.interceptor';
import { apiUrlInterceptor } from './api-url.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideZonelessChangeDetection(),
    provideRouter(routes, withViewTransitions(), withComponentInputBinding()),
    provideHttpClient(withInterceptors([
      apiUrlInterceptor, // rewrites /api/* to full Container Apps URL in production
      authInterceptor,   // adds Authorization header on every outgoing request
      errorInterceptor,  // maps HttpErrorResponse → AppError after retries are exhausted
      retryInterceptor,  // closest to backend; retries GET 5xx/network before error sees them
    ])),
  ]
};
