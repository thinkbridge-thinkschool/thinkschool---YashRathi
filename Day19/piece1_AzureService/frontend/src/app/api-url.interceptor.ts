import { HttpInterceptorFn } from '@angular/common/http';
import { environment } from '../environments/environment';

// In production, rewrites relative /api/* calls to the full Container Apps URL.
// In development, /api/* is handled by proxy.conf.json — this interceptor is a no-op.
export const apiUrlInterceptor: HttpInterceptorFn = (req, next) => {
  if (environment.production && req.url.startsWith('/api')) {
    return next(req.clone({ url: environment.apiBaseUrl + req.url.slice(4) }));
  }
  return next(req);
};
