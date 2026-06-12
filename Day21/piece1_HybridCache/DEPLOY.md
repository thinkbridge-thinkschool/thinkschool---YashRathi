# Deployment Guide — Azure Static Web Apps

## Architecture overview

```
Browser (Angular — static)
  │  calls relative /api/*
  ▼
Azure SWA backend (api/ Azure Function)
  │  acquires MI token via DefaultAzureCredential — NO secret
  │  adds Authorization: Bearer <MI token>
  ▼
Container Apps API
  https://piece33-verifyappinsights.happyhill-feb8a1b3.southeastasia.azurecontainerapps.io
```

- The Angular browser app handles **zero** tokens.
- Managed Identity logic is **backend-only** (api/src/functions/proxy.ts).
- The Container Apps URL never appears in browser code.

---

## Step 1 — Create the Azure Static Web Apps resource

1. Go to **portal.azure.com → Create a resource → Static Web App**.
2. Fill in:
   - **Subscription / Resource group** — your existing group.
   - **Name** — e.g. `quotes-swa`.
   - **Plan type** — **Standard** (required for Managed Identity on managed functions).
   - **Region** — e.g. `East Asia` (close to your Container Apps in Southeast Asia).
   - **Source** — GitHub → authorize, pick your repo, branch `main`.
   - **Build details**:
     - App location: `frontend`
     - Api location: `api`
     - Output location: `dist/quotes-ui/browser`
3. Click **Review + create** → **Create**.
4. After creation, Azure adds the deployment workflow automatically OR you use the
   one at `.github/workflows/azure-static-web-apps.yml` already in this repo.

---

## Step 2 — Copy the deployment token to GitHub Secrets

> **This is the ONLY secret in the pipeline. It is a deployment credential, not
> an API key or client secret.**

1. In the SWA resource: **Settings → Deployment tokens → Manage deployment token**.
2. Copy the token.
3. In your GitHub repo: **Settings → Secrets and variables → Actions → New repository secret**.
   - Name: `AZURE_STATIC_WEB_APPS_API_TOKEN`
   - Value: paste the token.

---

## Step 3 — Enable Managed Identity on the SWA resource

> Requires the **Standard** plan. Free plan does NOT support MI on managed functions.

1. In the SWA resource: **Settings → Identity**.
2. On the **System assigned** tab, set **Status** to **On** → **Save**.
3. Copy the **Object (principal) ID** — you need it in Step 4.

---

## Step 4 — Register the Container Apps API in Azure AD and grant MI access

For `DefaultAzureCredential` to acquire a token accepted by the Container Apps
API, the API must be registered in Azure AD and must validate Azure AD tokens.

### 4a. Register the API app

1. **Azure AD → App registrations → New registration**.
   - Name: `quotes-api` (or any name).
   - Supported account types: single tenant.
2. After creation, go to **Expose an API → Set Application ID URI** (e.g. `api://quotes-api`).
3. Click **Add a scope** → name it `access_as_user` (or `default`). Save.
4. Note the **Application ID URI** — this becomes `API_SCOPE`.

### 4b. Grant the SWA Managed Identity access to the API

1. In the Container Apps API's Azure AD app registration:
   **App roles → Create app role** — e.g. `API.Access`, allowed for Applications.
2. In **Azure AD → Enterprise applications → your SWA's MI** (search by Object ID from Step 3).
3. Go to **Permissions → Grant admin consent** for the app role.

### 4c. Set API_SCOPE in GitHub Variables (not a secret)

The scope is a public Azure AD resource identifier — it is not a secret.

1. In your GitHub repo: **Settings → Secrets and variables → Actions → Variables tab**.
2. Add:
   - Name: `API_SCOPE`
   - Value: `api://quotes-api/.default` (replace with your actual Application ID URI + `/.default`)

### 4d. Configure the SWA app setting

In the SWA resource: **Settings → Environment variables → Add**:
- Name: `API_SCOPE`
- Value: `api://quotes-api/.default`

---

## Step 5 — Configure the Container Apps API to accept Azure AD tokens

In the Container Apps resource:
1. **Settings → Authentication → Add identity provider → Microsoft**.
2. Set **App (client) ID** to the quotes-api app registration's client ID.
3. Set **Issuer URL**: `https://login.microsoftonline.com/<your-tenant-id>/v2.0`
4. Set **Unauthenticated requests**: `HTTP 401 Unauthorized`.

> Until this step is done, the SWA function acquires the MI token but the
> Container Apps API rejects it with 401 (it still validates its own JWT). The
> proxy code in api/src/functions/proxy.ts handles this gracefully and returns
> a 502 to the browser.

---

## Step 6 — Verify the deployment

After pushing to `main` the GitHub Actions workflow:
1. Installs Angular dependencies.
2. Runs `ng build --configuration production`.
3. Deploys `frontend/dist/quotes-ui/browser` + the `api/` function to SWA.

Check:
```
https://<your-swa-name>.azurestaticapps.net/          → Angular app loads
https://<your-swa-name>.azurestaticapps.net/api/proxy  → Function is live
```

Verify zero secrets: search the repo for `password`, `secret`, `key`, `token` —
the only hit should be `AZURE_STATIC_WEB_APPS_API_TOKEN` referenced in the
workflow, never stored in code.

---

## Step 7 — Custom domain (manual steps in Azure portal)

### 7a. Add the custom domain

1. In the SWA resource: **Settings → Custom domains → Add**.
2. Enter your domain, e.g. `quotes.yourdomain.com`.
3. Azure shows one of two validation methods:

**Method A — CNAME validation** (for subdomains, recommended)

| Type  | Host              | Value                                    |
|-------|-------------------|------------------------------------------|
| CNAME | `quotes`          | `<your-swa-name>.azurestaticapps.net`    |
| TXT   | `_dnsauth.quotes` | `<validation-token shown by Azure>`      |

Add both records at your DNS provider (Cloudflare, Route 53, GoDaddy, etc.).

**Method B — TXT validation** (if CNAME conflicts with an existing record)

| Type | Host                         | Value                              |
|------|------------------------------|------------------------------------|
| TXT  | `_dnsauth.quotes`            | `<validation-token shown by Azure>`|

Then separately add:

| Type  | Host     | Value                                 |
|-------|----------|---------------------------------------|
| CNAME | `quotes` | `<your-swa-name>.azurestaticapps.net` |

### 7b. Root domain (apex domain)

Azure SWA does not support CNAME for apex domains. Use:

| Type  | Host | Value                                 |
|-------|------|---------------------------------------|
| ALIAS | `@`  | `<your-swa-name>.azurestaticapps.net` |
| TXT   | `_dnsauth` | `<validation-token shown by Azure>` |

> ALIAS records are supported by Cloudflare (as a CNAME on `@`), Route 53
> (ALIAS), and Netlify DNS. Check your DNS provider's docs.

### 7c. Validate and activate

1. Back in the Azure portal, click **Validate** after adding DNS records.
2. DNS propagation can take up to 48 hours (usually < 10 minutes on modern DNS).
3. Azure provisions a free TLS certificate automatically — no manual cert needed.
4. Once validated, the domain shows **Validated** status in the SWA portal.

---

## Security checklist

| Check | Status |
|-------|--------|
| No client secret in code | ✅ |
| No API key in code | ✅ |
| No connection string in code | ✅ |
| No token in localStorage (MI path) | ✅ |
| No raw Container Apps URL in browser bundle | ✅ |
| Deployment token only in GitHub Secrets | ✅ |
| MI token acquired server-side only | ✅ |
| API_SCOPE stored as variable (non-secret) | ✅ |

---

## Local development

The Angular dev server proxies `/api/*` to `http://localhost:5051` via
`frontend/proxy.conf.json`. MI is NOT used locally — the .NET backend handles
auth directly.

```bash
# Terminal 1 — backend
cd backend && dotnet run

# Terminal 2 — frontend
cd frontend && npm start
```

For local function testing:
```bash
cd api && npm install && npm run build && func start
```
`DefaultAzureCredential` falls back to `az login` credentials locally, so run
`az login` once before testing the function locally.
