import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

export const authGuard: CanActivateFn = (_route, _state) => {
  const router = inject(Router);
  return localStorage.getItem('access_token') !== null
    ? true
    : router.parseUrl('/login');
};
