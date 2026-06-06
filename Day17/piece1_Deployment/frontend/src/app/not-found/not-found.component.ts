import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-not-found',
  standalone: true,
  imports: [RouterLink],
  template: `
    <div style="text-align:center;padding:6rem 2rem;min-height:100vh;background:#f5f3ff">
      <div style="font-size:6rem;font-weight:700;line-height:1;margin-bottom:1rem;
                  background:linear-gradient(135deg,#6366f1,#a855f7);
                  -webkit-background-clip:text;-webkit-text-fill-color:transparent">
        404
      </div>
      <h1 style="font-size:1.5rem;font-weight:700;color:#1e1b4b;margin-bottom:0.75rem">
        Page not found
      </h1>
      <p style="color:#6b7280;margin-bottom:2.5rem">
        The page you're looking for doesn't exist.
      </p>
      <a routerLink="/quotes"
         style="display:inline-flex;align-items:center;gap:0.5rem;
                background:linear-gradient(135deg,#4f46e5,#7c3aed);
                color:#fff;text-decoration:none;padding:0.65rem 1.5rem;
                border-radius:999px;font-size:0.875rem;font-weight:500;
                box-shadow:0 4px 12px rgba(79,70,229,0.3)">
        ← Back to Quotes
      </a>
    </div>
  `
})
export class NotFoundComponent {}
