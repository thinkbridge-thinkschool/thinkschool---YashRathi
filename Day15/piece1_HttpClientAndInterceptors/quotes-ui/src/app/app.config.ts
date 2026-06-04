import { ApplicationConfig, provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { authInterceptor } from './auth/auth.interceptor';
import { errorInterceptor } from './error.interceptor';
import { retryInterceptor } from './retry.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideZonelessChangeDetection(),
    provideHttpClient(withInterceptors([
      authInterceptor,   // adds Authorization header on every outgoing request
      errorInterceptor,  // maps HttpErrorResponse → AppError after retries are exhausted
      retryInterceptor,  // closest to backend; retries GET 5xx/network before error sees them
    ])),
  ]
};
