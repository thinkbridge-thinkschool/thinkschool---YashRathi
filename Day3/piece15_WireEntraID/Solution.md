# Day 3 · Piece 15 — Wire Entra ID as Identity Provider

## What changed

`InfrastructureExtensions.cs` now registers **two JWT Bearer schemes** plus a
**policy scheme** that picks between them at runtime.

| Scheme | When used | How validated |
|--------|-----------|---------------|
| `InternalJwt` | Internal callers / service-to-service | HMAC-SHA256 key from `appsettings.json` |
| `EntraId` | SPA users / customer-facing callers | RS256 via Entra OIDC discovery (`Authority`) |
| `MultiScheme` | Default scheme (routes to one of the above) | Peeks at `iss` claim before forwarding |

## How the routing works

`AddPolicyScheme` registers a thin scheme whose only job is to look at the
Bearer token's `iss` claim (without validating it first) and forward to the
right real scheme:

```
iss contains "login.microsoftonline.com"  →  EntraId
otherwise                                 →  InternalJwt
```

Because `AddAuthentication(MultiScheme)` makes `MultiScheme` the default, all
`RequireAuthorization()` calls automatically go through this router.

## Why not Microsoft.Identity.Web?

The task calls for `AddJwtBearer` with `Authority` directly — the same approach
works without the `Microsoft.Identity.Web` convenience wrapper and keeps the
dependency surface small.  `Authority` is enough: the middleware auto-fetches
signing keys from
`https://login.microsoftonline.com/{tenant}/v2.0/.well-known/openid-configuration`
on first use and caches them.

## Configuration (appsettings.json)

```jsonc
"EntraId": {
  "TenantId":  "<Azure AD tenant GUID>",
  "ClientId":  "<Application (client) ID>",
  "Audience":  "api://<ClientId>",   // or just the ClientId depending on how you expose the API
  "Instance":  "https://login.microsoftonline.com/"
}
```

These values come from **Azure Portal → App registrations → your API app**.

## Testing

### Internal JWT (existing flow)
```bash
TOKEN=$(curl -s -X POST http://localhost:5000/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"email":"test@example.com","password":"password123"}' \
  | jq -r '.accessToken')

curl -H "Authorization: Bearer $TOKEN" http://localhost:5000/api/quotes
```

### Entra ID token (SPA / customer-facing)
```bash
# Acquire token via Azure CLI (replace YOUR_APP_ID with the Application ID URI)
ENTRA_TOKEN=$(az account get-access-token --resource api://YOUR_CLIENT_ID \
  --query accessToken -o tsv)

curl -H "Authorization: Bearer $ENTRA_TOKEN" http://localhost:5000/api/quotes
```

The policy scheme reads the `iss` claim and forwards to `EntraId` — the
middleware then validates signature via Entra's public keys.

## Security notes

* The internal signing key must be rotated and stored in a secret manager
  (Azure Key Vault / environment variable) in production — never committed.
* `ClockSkew = TimeSpan.Zero` is kept on both schemes for strict expiry.
* Entra ID handles password hygiene, MFA, and token revocation for SPA users;
  we only need to trust the signed JWT.
