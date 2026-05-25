# Day 5 — Piece 35: Smoke-Test the Deployed API + Week 1 Reflection

## Live API URL

```
https://piece33-verifyappinsights.happyhill-feb8a1b3.southeastasia.azurecontainerapps.io/health
```

---

## Smoke-Test Results

All 18 test scenarios passed. Tests were run with PowerShell `Invoke-RestMethod` / `Invoke-WebRequest`.

### GET /health

```
Status  : 200
Body    : Healthy
```

---

### POST /api/auth/login

| Scenario | Status | Notes |
|---|---|---|
| Valid credentials (`test@example.com` / `password123`) | **200** | JWT + refresh token returned; `expiresIn=900` |
| Invalid credentials (wrong password) | **401** | Correct rejection |
| Duration (valid login) | **1143 ms** | BCrypt dominates — expected and intentional |

---

### GET /api/quotes?page=1&size=5

| Scenario | Status | Notes |
|---|---|---|
| Empty DB (cold container) | **200** | Returns `[]` — correct, not an error |
| After inserting a quote | **200** | Returns 1-item array |
| Duration (cold) | **264 ms** | Acceptable |

---

### GET /api/quotes/{id}

| Scenario | Status | Notes |
|---|---|---|
| `/api/quotes/1` — empty DB | **404** | Correct |
| `/api/quotes/1` — after insert | **200** | Returns full quote object in ~67 ms |
| `/api/quotes/99999` — non-existent | **404** | Correct |

---

### POST /api/quotes

| Scenario | Status | Notes |
|---|---|---|
| No `Authorization` header | **401** | Gate works |
| Valid JWT, valid body | **201** | `Location: /api/quotes/1`; round-trip ~322 ms |
| Valid JWT, empty `text` | **400** | Domain validation fires (`Quote.Create` guard) |

---

### POST /api/auth/refresh

| Scenario | Status | Notes |
|---|---|---|
| Valid refresh token | **200** | New access + refresh tokens returned in ~156 ms |
| Old (already-used) token | **401** | **Refresh-token reuse detection working** — entire family revoked |

---

### POST /api/auth/logout

| Scenario | Status |
|---|---|
| Valid refresh token | **204** |

---

### DELETE /api/quotes/{id}

| Scenario | Status | Notes |
|---|---|---|
| No `Authorization` header | **401** | Correct |
| Owner deletes own quote | **204** | ~83 ms; soft-delete confirmed |
| Verify deleted quote via GET | **404** | Soft-delete hides the record |
| Non-existent ID | **404** | Correct |

---

## Summary Table

| Endpoint | Method | Expected | Got | Pass |
|---|---|---|---|---|
| `/health` | GET | 200 | 200 | ✓ |
| `/api/auth/login` | POST (valid) | 200 | 200 | ✓ |
| `/api/auth/login` | POST (invalid) | 401 | 401 | ✓ |
| `/api/quotes` | GET (empty DB) | 200 | 200 | ✓ |
| `/api/quotes` | GET (after insert) | 200 | 200 | ✓ |
| `/api/quotes/1` | GET (empty) | 404 | 404 | ✓ |
| `/api/quotes/1` | GET (exists) | 200 | 200 | ✓ |
| `/api/quotes/99999` | GET | 404 | 404 | ✓ |
| `/api/quotes` | POST (no auth) | 401 | 401 | ✓ |
| `/api/quotes` | POST (valid) | 201 | 201 | ✓ |
| `/api/quotes` | POST (bad body) | 400 | 400 | ✓ |
| `/api/auth/refresh` | POST (valid) | 200 | 200 | ✓ |
| `/api/auth/refresh` | POST (reused) | 401 | 401 | ✓ |
| `/api/auth/logout` | POST | 204 | 204 | ✓ |
| `/api/quotes/2` | DELETE (no auth) | 401 | 401 | ✓ |
| `/api/quotes/2` | DELETE (owner) | 204 | 204 | ✓ |
| `/api/quotes/99999` | DELETE | 404 | 404 | ✓ |
| `/api/quotes/2` | GET (after delete) | 404 | 404 | ✓ |

**18/18 passed.**

---

## Fragile Spots — Things That Feel Risky

| Fragile Spot | Risk | Mitigation |
|---|---|---|
| **SQLite ephemeral storage** | Container restart wipes all data; seed user is re-created but quotes are lost | Switch to Azure SQL / PostgreSQL with persistent storage for any real usage |
| **BCrypt login at ~1 s** | Slow but correct; one concurrent brute-force thread can saturate a single container | Add rate-limiting middleware (`AddRateLimiter`) on `POST /api/auth/login` |
| **Single-node, single-container** | No horizontal scaling; one bad deploy takes the API down | Enable Container Apps scaling rules (min-replicas ≥ 2) |
| **ExternalQuoteService dummy URL** | `https://api.quotetags.example.com` does not exist; Polly will retry + circuit-break on every call | Wire a real upstream or keep behind a feature flag until an upstream exists |
| **JWT signing key in Container Apps secret** | Key is static; rotation requires a redeploy | Use Azure Key Vault + JWKS endpoint for zero-downtime key rotation |
| **No HTTPS redirect enforcement** | Requests to HTTP port 80 are not tested | Azure Container Apps terminates TLS at the ingress, but confirm `UseHttpsRedirection` in non-ACA hosts |
| **Validation error body empty** | `Results.ValidationProblem` returns 400 but the body may not surface through some proxies | Test problem-detail bodies in integration tests; confirm `AddProblemDetails()` is wired |

---

## Week 1 Reflection

In this week felt a bit challenging as that of normal college projects, it was because the course outline absolutely covers problems which needed critical thinking. I faced challenges and tackled it with multiple strategic approaches. During this I worked on real world backend project, also deployed it on Azure. It took me just six days to build and deploy an ASP .NET API with JWT authentication, EF Core, testing, logging, tracing, CI/CD, and container deployment. I got a lot of confidence by seeing my API running live on Azure.
According to me debugging and setup was the biggest help of the week I took from AI. Many areas like JWT auth, OpenTelemetry, Serilog, Application Insights and Azure deployment were known to me but was first time I implemented it. It also helped me save time while I was writing setup code, fixing errors, understanding how different services work together and writing tests and refactoring code was much faster than usual.
While practicing, I learned that I should not follow every suggestion blindly. Sometimes configuration works locally but fails inside the container because of missing environment variable or may be runtime issues, I also faced problem with JWT secret key configuration. I found these issues by checking logs, testing endpoints and running the project step by step. I personally feel that it improved my debugging skills a lot.
The topic I feel I can improve on is distributed tracing and observability. In it I understood basic logs and traces very well but I still want my hands to be better with OpenTelemetry, correlation IDs and Azure Monitor. Before week 2 starts I have plan of revising these concepts and practice it.
The program surely improved my teamwork and communication skills. From my first day itself I interacted with mentors and other interns regularly; I discussed problems, shared ideas and learned about new approaches towards single problem. Which helped me understand that single problem may have multiple solutions but we need to go with the most optimal and efficient one. All friends I made here will surely help me grow in further journey. I got an environment which makes learning experience more enjoyable and motivating.
Time management is another big learning I got during this program. In my first week itself for some days I worked for almost 12 hours continuously on coding, debugging, deployment and documentation. Managing time was crucial because each task was dependant on the previous one. I stayed consistent, focused on priorities and completed work within deadline.
I was feeling pretty confident after I was able to build and deploy real world projects within such small timeframe. I am excited to face the further challenges in upcoming tasks and surely I am improving my skillset and giving my best to reach the best possible solutions to the problems. I am able to see the software engineer inside me who is having spirit of contributing  to company.
