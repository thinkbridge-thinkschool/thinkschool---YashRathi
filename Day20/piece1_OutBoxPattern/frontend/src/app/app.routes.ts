import { Routes } from '@angular/router';
import { QuotesListPageComponent } from './quotes/quotes-list-page.component';
import { LoginComponent } from './auth/login.component';
import { authGuard } from './guards/auth.guard';

export const routes: Routes = [
  { path: '', redirectTo: 'quotes', pathMatch: 'full' },
  // Public: anyone can browse the list
  { path: 'quotes', component: QuotesListPageComponent },
  // Protected: reading full detail requires a valid session
  {
    path: 'quotes/:id',
    loadComponent: () =>
      import('./quote-detail/quote-detail.component').then(m => m.QuoteDetailComponent),
    canActivate: [authGuard]
  },
  { path: 'login', component: LoginComponent },
  {
    path: '**',
    loadComponent: () =>
      import('./not-found/not-found.component').then(m => m.NotFoundComponent)
  }
];
