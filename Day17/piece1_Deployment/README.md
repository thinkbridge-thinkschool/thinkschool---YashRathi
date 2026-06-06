# Day 17 — Deploy to Azure Static Web Apps

Angular 21 frontend deployed to Azure Static Web Apps, calling the Week-1 Container Apps API via a Managed Identity proxy with zero stored secrets.

---

## Live URLs

| Resource | URL |
|----------|-----|
| Frontend (SWA) | https://yellow-meadow-0bd239f00.7.azurestaticapps.net |
| Backend (Container Apps) | https://piece33-verifyappinsights.happyhill-feb8a1b3.southeastasia.azurecontainerapps.io |
| Health check | https://piece33-verifyappinsights.happyhill-feb8a1b3.southeastasia.azurecontainerapps.io/health |

---

## Architecture

```
Browser (Angular 21 — Azure Static Web Apps)
  │
  │  calls relative /api/* in dev
  │  apiUrlInterceptor rewrites to Container Apps URL in production
  │
  ▼
Container Apps API (.NET 10)
  https://piece33-verifyappinsights.happyhill-feb8a1b3.southeastasia.azurecontainerapps.io
  │
  ├── GET  /api/quotes?page=1&size=10   → [{ id, author, text, createdAt }]
  ├── GET  /api/quotes/{id}             → { id, author, text, createdAt }
  ├── POST /api/quotes                  → 201 { id, author, text, createdAt }
  ├── DELETE /api/quotes/{id}           → 204 No Content
  ├── POST /api/auth/login              → { accessToken, refreshToken, expiresIn }
  └── GET  /health                      → "Healthy"
```

### Managed Identity architecture (Standard plan)

When upgraded to SWA Standard plan, the MI proxy replaces direct browser calls:

```
Browser → /api/* → SWA backend function (api/)
                        │
                        │  DefaultAzureCredential — NO secret
                        │  acquires MI token at runtime
                        ▼
                   Container Apps API
                   Authorization: Bearer <MI token>
```

---

## Project structure

```
piece1_Deployment/
├── frontend/                        # Angular 21 app
│   ├── src/
│   │   ├── app/
│   │   │   ├── api-url.interceptor.ts   # rewrites /api/* → Container Apps URL in prod
│   │   │   ├── auth/                    # login, auth interceptor, auth service
│   │   │   ├── quotes/                  # quotes list page
│   │   │   ├── quote-detail/            # single quote view (auth-guarded)
│   │   │   ├── quote-create/            # create quote form (Signal Forms)
│   │   │   ├── stores/quotes.store.ts   # signal-based state store
│   │   │   └── app.config.ts            # providers + interceptor chain
│   │   ├── environments/
│   │   │   ├── environment.ts           # dev: apiBaseUrl = /api (proxy)
│   │   │   └── environment.prod.ts      # prod: apiBaseUrl = Container Apps URL
│   │   └── index.html                   # SEO meta tags for Lighthouse ≥ 95
│   └── public/
│       └── staticwebapp.config.json     # SPA fallback, security headers, CSP
├── api/                             # SWA backend function (MI proxy)
│   ├── src/functions/proxy.ts       # DefaultAzureCredential — zero secrets
│   ├── host.json
│   ├── package.json
│   └── tsconfig.json
├── backend/                         # .NET 10 Container Apps API
│   ├── Program.cs                   # CORS + middleware pipeline
│   ├── Dockerfile                   # production Docker image
│   └── ...
├── .github/workflows/
│   ├── ci.yml                       # .NET tests + coverage
│   └── azure-static-web-apps.yml    # SWA deploy on push to main
└── DEPLOY.md                        # custom domain + DNS + MI activation steps
```

---

## API fields

All quote objects use exactly these fields:

```typescript
interface Quote {
  id: number;
  author: string;
  text: string;
  createdAt: string;
}
```

---

## Auth model

| Layer | Mechanism |
|-------|-----------|
| User login | POST /api/auth/login → JWT (scope: quotes.write) |
| Protected routes | authGuard checks localStorage for token |
| Outgoing requests | authInterceptor adds Authorization: Bearer \<token\> |
| MI proxy (Standard plan) | DefaultAzureCredential — no secret, no key |

Test credentials:
- Email: `test@example.com`
- Password: `password123`

---

## Interceptor chain

```
apiUrlInterceptor   → rewrites /api/* to full Container Apps URL in production
authInterceptor     → adds Authorization: Bearer <user JWT> if logged in
errorInterceptor    → maps HttpErrorResponse → AppError with friendly message
retryInterceptor    → exponential backoff (GET only, 3 retries, 5xx/network)
```

---

## Security — zero secrets

| Check | Status |
|-------|--------|
| No client secret in code | ✅ |
| No API key in code | ✅ |
| No connection string in code | ✅ |
| No token in localStorage (MI path) | ✅ |
| Container Apps URL in browser code | ✅ (environment.prod.ts — not a secret) |
| Deployment token in GitHub Secrets only | ✅ |
| MI token acquired server-side only | ✅ (api/src/functions/proxy.ts) |

---

## Local development

```bash
# Terminal 1 — backend (.NET)
cd backend
dotnet run

# Terminal 2 — frontend (Angular)
cd frontend
npm install
npm start
# Runs on http://localhost:4200
# /api/* proxied to http://localhost:5000 via proxy.conf.json
```

---

## Deploy frontend to SWA (manual)

```bash
# Build
cd frontend
npm run build -- --configuration production

# Deploy
swa deploy ./dist/quotes-ui/browser \
  --deployment-token <AZURE_STATIC_WEB_APPS_API_TOKEN> \
  --env production
```

---

## Deploy backend to Container Apps

```bash
# Login
az login
az acr login --name cr5qulxll7yezxo

# Build and push Docker image
docker build -t cr5qulxll7yezxo.azurecr.io/piece33-verifyappinsights/piece33-verifyappinsights-dev:latest ./backend
docker push cr5qulxll7yezxo.azurecr.io/piece33-verifyappinsights/piece33-verifyappinsights-dev:latest

# Update Container App
az containerapp update \
  --name piece33-verifyappinsights \
  --resource-group rg-dev \
  --image cr5qulxll7yezxo.azurecr.io/piece33-verifyappinsights/piece33-verifyappinsights-dev:latest
```

---

## CI/CD

| Workflow | Trigger | What it does |
|----------|---------|--------------|
| `ci.yml` | push to any branch | .NET build + test + 70% coverage check |
| `azure-static-web-apps.yml` | push to main | Angular production build + SWA deploy |

---

## Lighthouse score

Measured on live SWA URL: `https://yellow-meadow-0bd239f00.7.azurestaticapps.net/quotes`

| Category | Score |
|----------|-------|
| Performance | 96 |
| Accessibility | 100 |
| Best Practices | 100 |
| SEO | 91 |

Screenshot: [screenshots/04-lighthouse-score.png](screenshots/04-lighthouse-score.png)

Optimisations applied:
- Production build with full tree-shaking and minification
- Lazy-loaded routes (`quote-detail`, `not-found`)
- Output hashing for long-term cache (`max-age=31536000, immutable`)
- SEO meta tags: description, OG tags, Twitter Card, theme-color
- Security headers via `staticwebapp.config.json`
- Initial bundle: 367 kB raw / 94 kB gzipped (well under 500 kB budget)

---

## Screenshots

| # | Screenshot | What it shows |
|---|-----------|---------------|
| 01 | [screenshots/01-tsc-zero-errors.png](screenshots/01-tsc-zero-errors.png) | TypeScript zero errors |
| 02 | [screenshots/02-store-ngrx-rule.png](screenshots/02-store-ngrx-rule.png) | NgRx store rule |
| 03 | [screenshots/03-browser-loading-state.png](screenshots/03-browser-loading-state.png) | Browser loading state |
| 04 | [screenshots/04-lighthouse-score.png](screenshots/04-lighthouse-score.png) | Lighthouse 96/100/100/91 on live SWA URL |
| 05 | [screenshots/05-quotes-list.png](screenshots/05-quotes-list.png) | Live quotes list — Shakespeare, Jobs, Wilde, Rowling loaded from Container Apps API |
| 06 | [screenshots/06-login-page.png](screenshots/06-login-page.png) | Login page with test credentials |
| 07 | [screenshots/07-quote-added.png](screenshots/07-quote-added.png) | "Quote added successfully" — POST /api/quotes working end-to-end |
| 08 | [screenshots/08-search-filter.png](screenshots/08-search-filter.png) | Author search filter — "Yas" returns Yash Rathi's quote |

---

## Activating Managed Identity end-to-end

The MI proxy code is fully written in `api/src/functions/proxy.ts`.
To activate it:

1. Upgrade SWA to **Standard plan** (~$9/month)
2. Enable System Assigned MI on the SWA resource
3. Register the Container Apps API in Azure AD
4. Set `API_SCOPE` as a non-secret app setting
5. Configure Container Apps EasyAuth to accept Azure AD tokens

See [DEPLOY.md](DEPLOY.md) for step-by-step instructions.

---

## What breaks if the API changes

| Change | Impact |
|--------|--------|
| `/api/quotes` renamed | 404 from upstream → error state in UI |
| Auth scheme changed | MI token rejected → 401/502 → error state in UI |
| Container Apps URL changes | Update `environment.prod.ts` + `api/src/functions/proxy.ts` |
| JWT signing key rotated | All existing user sessions invalidated → re-login required |
