# Day 5 — Piece 34: Polly Resilience on HTTP Calls

## Package added

```
Microsoft.Extensions.Http.Resilience 10.6.0
  └─ Microsoft.Extensions.Resilience 10.6.0
       └─ Polly.Core 8.4.2
       └─ Polly.Extensions 8.4.2
```

---

## HttpClient + resilience handler config

**`Extensions/InfrastructureExtensions.cs`** (additions)

```csharp
services.AddHttpClient(ExternalQuoteService.ClientName, client =>
{
    client.BaseAddress = new Uri("https://api.quotetags.example.com");
    // Polly owns total-request timing — disable HttpClient's own timeout.
    client.Timeout = Timeout.InfiniteTimeSpan;
})
.AddResilienceHandler("default", (ResiliencePipelineBuilder<HttpResponseMessage> builder,
                                   ResilienceHandlerContext ctx) =>
{
    var logger = ctx.ServiceProvider
        .GetRequiredService<ILogger<ExternalQuoteService>>();

    // 1. Retry — 3 attempts, exponential backoff + jitter, log every retry.
    builder.AddRetry(new HttpRetryStrategyOptions
    {
        MaxRetryAttempts = 3,
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,
        Delay = TimeSpan.FromMilliseconds(200),   // use ≥1 s in real production
        OnRetry = args =>
        {
            logger.LogWarning(
                "HTTP retry {Attempt}/{Max} after {Delay}ms — " +
                "{Method} {Url} responded {Reason}",
                args.AttemptNumber + 1, 3,
                args.RetryDelay.TotalMilliseconds,
                args.Outcome.Result?.RequestMessage?.Method,
                args.Outcome.Result?.RequestMessage?.RequestUri,
                args.Outcome.Exception?.Message
                    ?? args.Outcome.Result?.ReasonPhrase);
            return ValueTask.CompletedTask;
        }
    });

    // 2. Circuit breaker — opens after ≥50 % failures over a 30-second window
    //    (minimum 3 requests before tripping).
    builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
    {
        SamplingDuration = TimeSpan.FromSeconds(30),
        FailureRatio = 0.5,
        MinimumThroughput = 3,
        BreakDuration = TimeSpan.FromSeconds(10)
    });

    // 3. Per-attempt timeout — 10 s.
    builder.AddTimeout(TimeSpan.FromSeconds(10));
});

services.AddTransient<IExternalQuoteService, ExternalQuoteService>();
```

### Handler chain

```
Request
  └─ ResiliencePipeline  (outermost DelegatingHandler)
       ├─ Retry          (3 attempts, exponential + jitter, logs every retry)
       ├─ CircuitBreaker (opens at 50 % failure rate / 30 s window)
       └─ Timeout        (10 s per attempt)
            └─ HttpClientHandler  (primary — real network / test stub)
```

---

## Service

**`Services/ExternalQuoteService.cs`**

```csharp
public sealed class ExternalQuoteService(IHttpClientFactory factory) : IExternalQuoteService
{
    public const string ClientName = "external-quotes";

    public async Task<string[]> GetTagsAsync(int quoteId, CancellationToken ct = default)
    {
        var client = factory.CreateClient(ClientName);
        var response = await client.GetAsync($"/quotes/{quoteId}/tags", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<string[]>(ct) ?? [];
    }
}
```

---

## Test — forcing transient failures

**`QuotesApi.Tests/ResilienceTests.cs`**

`TransientFailureHandler` is injected as the **primary** handler via
`ConfigurePrimaryHttpMessageHandler`. The resilience pipeline (retry / CB / timeout)
sits above it; every retry goes through the stub again.

```csharp
// Stub: returns 503 for the first failCount calls, then 200
internal sealed class TransientFailureHandler : HttpMessageHandler
{
    private int _callCount;
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage req, CancellationToken ct)
    {
        var n = Interlocked.Increment(ref _callCount);
        var resp = (_alwaysFail || n <= _failCount)
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : new HttpResponseMessage(HttpStatusCode.OK)
              { Content = new StringContent("[]", Encoding.UTF8, "application/json") };
        return Task.FromResult(resp);
    }
}

// Fixture: keeps the resilience pipeline, replaces only the primary handler
services.AddHttpClient(ExternalQuoteService.ClientName)
    .ConfigurePrimaryHttpMessageHandler(() => Handler);
```

### Test 1 — recovers after two transient failures

```
Assertion : tags != null  AND  CallCount == 3
Result    : PASSED in 1 s
```

### Test 2 — never recovers; circuit breaker trips

```
Assertion : throws Exception  AND  CallCount == 3
Result    : PASSED in 6 s
```

---

## Retry logs (captured from dotnet test run)

### Test 1: stub fails twice → succeeds on attempt 3

```log
[WRN] Polly Execution attempt.
        Source='external-quotes-default//Retry' Result='503' Handled='True' Attempt='0'
[WRN] Polly Resilience event occurred. EventName='OnRetry' Result='503'
[WRN] QuotesApi.Services.ExternalQuoteService
        HTTP retry 1/3 after 224ms — null null responded Service Unavailable

[WRN] Polly Execution attempt.
        Source='external-quotes-default//Retry' Result='503' Handled='True' Attempt='1'
[WRN] Polly Resilience event occurred. EventName='OnRetry' Result='503'
[WRN] QuotesApi.Services.ExternalQuoteService
        HTTP retry 2/3 after 89ms — null null responded Service Unavailable

[INF] Polly Execution attempt.
        Source='external-quotes-default//Retry' Result='200' Handled='False' Attempt='2'
```

Attempt 2 (zero-indexed) returns 200 → success, no further retries.

---

### Test 2: stub always fails → circuit opens on attempt 4

```log
[WRN] Polly Execution attempt.
        Source='external-quotes-default//Retry' Result='503' Handled='True' Attempt='0'
[WRN] Polly Resilience event occurred. EventName='OnRetry' Result='503'
[WRN] QuotesApi.Services.ExternalQuoteService
        HTTP retry 1/3 after 252ms — null null responded Service Unavailable

[WRN] Polly Execution attempt.
        Source='external-quotes-default//Retry' Result='503' Handled='True' Attempt='1'
[WRN] Polly Resilience event occurred. EventName='OnRetry' Result='503'
[WRN] QuotesApi.Services.ExternalQuoteService
        HTTP retry 2/3 after 114ms — null null responded Service Unavailable

[ERR] Polly Resilience event occurred.
        EventName='OnCircuitOpened'
        Source='external-quotes-default//CircuitBreaker' Result='503'

[WRN] Polly Execution attempt.
        Source='external-quotes-default//Retry' Result='503' Handled='True' Attempt='2'
[WRN] Polly Resilience event occurred. EventName='OnRetry' Result='503'
[WRN] QuotesApi.Services.ExternalQuoteService
        HTTP retry 3/3 after 275ms — null null responded Service Unavailable

[INF] Polly Execution attempt.
        Source='external-quotes-default//Retry'
        Result='The circuit is now open and is not allowing calls.'
        Handled='False' Attempt='3'
Polly.CircuitBreaker.BrokenCircuitException: The circuit is now open and is not allowing calls.
```

3 failures → circuit opens (100 % failure rate > 50 % threshold) →
attempt 4 is rejected by the circuit breaker before reaching the stub →
`BrokenCircuitException` propagates (not retried; outside `HttpRetryStrategyOptions.ShouldHandle`).

---

## Key observations

| Property | Observed behaviour |
|---|---|
| **Retry 1–3** | Logged at `WARN` with exponential jitter delays (89–275 ms) |
| **Circuit breaker** | `OnCircuitOpened` fires at `ERR` after 3/3 = 100 % failure rate |
| **4th attempt short-circuited** | CB intercepts before the stub is called → handler `CallCount` stays at 3 |
| **No silent swallowing** | Every attempt logged; final exception propagates to caller |
| **No real network calls in tests** | Primary handler replaced — zero DNS lookups |

---

## Full test suite result

```
dotnet test QuotesApi.Tests/QuotesApi.Tests.csproj

Passed!  - Failed: 0, Passed: 19, Skipped: 0, Total: 19, Duration: 26 s
```

(2 new resilience tests + 17 pre-existing tests, all green)

---

