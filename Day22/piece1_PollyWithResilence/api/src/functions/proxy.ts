import { app, HttpRequest, HttpResponseInit, InvocationContext } from '@azure/functions';
import { DefaultAzureCredential } from '@azure/identity';

// ─── Configuration ──────────────────────────────────────────────────────────
// The upstream Container Apps API URL is stored here (server-side only).
// It is NOT sent to the browser and NOT a secret.
const UPSTREAM_BASE =
  'https://piece33-verifyappinsights.happyhill-feb8a1b3.southeastasia.azurecontainerapps.io';

// The Azure AD scope for the Container Apps API.
// Set API_SCOPE as an app setting in the SWA resource (not a secret — it is a
// public Azure AD resource identifier, e.g. api://<your-app-id>/.default).
// If the API is not yet registered in Azure AD, keep this as-is and register it
// before enabling Managed Identity end-to-end.
const API_SCOPE = process.env['API_SCOPE'] ?? `${UPSTREAM_BASE}/.default`;

// DefaultAzureCredential resolves the identity in this order (no secret needed):
//   1. Managed Identity (when running on Azure SWA — Standard plan required)
//   2. Azure CLI credentials (when running locally with `az login`)
//   3. VS Code / Azure PowerShell / etc.
// Zero secrets stored anywhere.
const credential = new DefaultAzureCredential();

// ─── Proxy handler ───────────────────────────────────────────────────────────
// Receives every request at /api/{*path} from the Angular browser app,
// acquires an MI token at runtime, and proxies to the Container Apps API.
async function proxyHandler(
  request: HttpRequest,
  context: InvocationContext,
): Promise<HttpResponseInit> {
  context.log(`[proxy] ${request.method} ${request.url}`);

  // 1. Acquire MI token — no secret, no hardcoded credential.
  let miToken: string;
  try {
    const tokenResponse = await credential.getToken(API_SCOPE);
    miToken = tokenResponse.token;
  } catch (err) {
    context.error('[proxy] MI token acquisition failed:', err);
    return {
      status: 502,
      jsonBody: { error: 'Could not acquire identity token for upstream API.' },
    };
  }

  // 2. Build the upstream URL.
  //    The route parameter captures everything after /api/ — e.g. for a
  //    browser request to /api/quotes?page=1, remainingPath = "quotes".
  const remainingPath = (request.params['path'] ?? '').replace(/^\/+/, '');
  const incomingUrl = new URL(request.url);
  const upstreamUrl = `${UPSTREAM_BASE}/api/${remainingPath}${incomingUrl.search}`;

  context.log(`[proxy] → upstream: ${upstreamUrl}`);

  // 3. Forward the request with the MI token as Authorization.
  //    The browser NEVER sees this token — it only travels server → upstream.
  const outHeaders: Record<string, string> = {
    Authorization: `Bearer ${miToken}`,
    'Content-Type': 'application/json',
    Accept: 'application/json',
  };

  let body: string | undefined;
  if (['POST', 'PUT', 'PATCH'].includes(request.method)) {
    body = await request.text();
  }

  let upstreamResponse: Response;
  try {
    upstreamResponse = await fetch(upstreamUrl, {
      method: request.method,
      headers: outHeaders,
      body,
    });
  } catch (err) {
    context.error('[proxy] Upstream fetch error:', err);
    return {
      status: 502,
      jsonBody: { error: 'Upstream API is unreachable.' },
    };
  }

  // 4. Return the upstream response verbatim to the Angular app.
  const contentType =
    upstreamResponse.headers.get('content-type') ?? 'application/json';
  const responseBody =
    upstreamResponse.status !== 204 ? await upstreamResponse.text() : undefined;

  return {
    status: upstreamResponse.status,
    headers: {
      'Content-Type': contentType,
      'Cache-Control': 'no-store',
    },
    body: responseBody,
  };
}

// ─── Function registration ────────────────────────────────────────────────────
// Route {*path} catches all sub-paths under /api/:
//   /api/quotes         → remainingPath = "quotes"
//   /api/quotes/5       → remainingPath = "quotes/5"
//   /api/auth/login     → remainingPath = "auth/login"
app.http('proxy', {
  methods: ['GET', 'POST', 'PUT', 'DELETE', 'PATCH', 'OPTIONS'],
  authLevel: 'anonymous',
  route: '{*path}',
  handler: proxyHandler,
});
