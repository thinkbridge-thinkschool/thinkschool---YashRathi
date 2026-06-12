using QuotesApi.Resilience;
using QuotesApi.Services;

namespace QuotesApi.Endpoints;

public static class ResilienceEndpoints
{
    public static IEndpointRouteBuilder MapResilienceEndpoints(this IEndpointRouteBuilder app)
    {
        // ── Stub downstream service ────────────────────────────────────────────
        // The Polly pipeline calls this endpoint.
        // Returns 200 normally; 500 when the FaultSwitch is on.
        app.MapGet("/api/stub/service", (FaultSwitch fault) =>
            fault.IsOn
                ? Results.Problem("Simulated downstream failure", statusCode: 500)
                : Results.Ok(new { status = "ok", ts = DateTimeOffset.UtcNow }))
           .ExcludeFromDescription();

        // ── Fault-switch controls ──────────────────────────────────────────────
        app.MapPost("/api/resilience/fault/on", (FaultSwitch fault) =>
        {
            fault.Enable();
            return Results.Ok(new { fault = "on", note = "Stub will return HTTP 500" });
        }).WithTags("Resilience");

        app.MapPost("/api/resilience/fault/off", (FaultSwitch fault) =>
        {
            fault.Disable();
            return Results.Ok(new { fault = "off", note = "Stub will return HTTP 200" });
        }).WithTags("Resilience");

        // ── Current status ─────────────────────────────────────────────────────
        app.MapGet("/api/resilience/status", (CircuitBreakerStateTracker tracker, FaultSwitch fault) =>
            Results.Ok(new
            {
                circuit = tracker.StateProvider.CircuitState.ToString(),
                fault = fault.IsOn ? "on" : "off",
                log = tracker.Log
            }))
           .WithTags("Resilience");

        // ── Single call through the full Polly pipeline ────────────────────────
        app.MapGet("/api/resilience/call", async (
            ExternalQuoteClient client,
            CircuitBreakerStateTracker tracker) =>
        {
            var (ok, body) = await client.GetAsync();
            return Results.Ok(new
            {
                ok,
                body,
                circuit = tracker.StateProvider.CircuitState.ToString()
            });
        }).WithTags("Resilience");

        // ── Automated prove scenario ───────────────────────────────────────────
        // Drives the full lifecycle: Closed → Open → Half-Open → Closed.
        //
        // Phase 1  — 10 concurrent failures → circuit opens
        // Phase 1b — 3 calls fired while circuit is CONFIRMED open
        //            → guaranteed BrokenCircuitException (no retry, no network hit)
        // Phase 2  — wait for BreakDuration (5 s) + 1 s buffer
        // Phase 3  — disable fault, probe half-open → circuit closes
        app.MapPost("/api/resilience/prove", async (
            ExternalQuoteClient client,
            FaultSwitch fault,
            CircuitBreakerStateTracker tracker) =>
        {
            tracker.ClearLog();
            tracker.LogEvent("START", "=== prove scenario begins ===");

            // ── Phase 1: saturate with failures ───────────────────────────────
            fault.Enable();
            tracker.LogEvent("PHASE-1", "FaultSwitch ON — firing 10 concurrent requests");

            var phase1 = await Task.WhenAll(
                Enumerable.Range(1, 10).Select(async i =>
                {
                    var (ok, body) = await client.GetAsync();
                    return new
                    {
                        req = i,
                        ok,
                        outcome = ok ? "SUCCESS" : body,
                        circuit = tracker.StateProvider.CircuitState.ToString()
                    };
                }));

            tracker.LogEvent("PHASE-1-END",
                $"Circuit after phase 1: {tracker.StateProvider.CircuitState}");

            // ── Phase 1b: call while OPEN → BrokenCircuitException ────────────
            // All 10 Phase-1 requests entered the CB before it tripped (they were
            // already inside the retry loop when the CB opened). Phase 1b fires
            // calls AFTER the circuit is confirmed open, so the CB rejects them
            // immediately — no retry, zero network hits, instant BrokenCircuitException.
            tracker.LogEvent("PHASE-1B",
                "Circuit OPEN — 3 calls to prove instant rejection (BrokenCircuitException)");

            var phase1b = new List<object>();
            for (int i = 1; i <= 3; i++)
            {
                var (ok, body) = await client.GetAsync();
                var state = tracker.StateProvider.CircuitState.ToString();
                phase1b.Add(new { req = i, ok, outcome = ok ? "SUCCESS" : body, circuit = state });
                tracker.LogEvent("OPEN-REJECT",
                    $"req={i} ok={ok} circuit={state} | {body[..Math.Min(60, body.Length)]}");
            }

            tracker.LogEvent("PHASE-1B-END",
                $"All 3 rejected instantly — no retries fired, no network hit");

            // ── Phase 2: wait for break duration ──────────────────────────────
            const int breakWaitSeconds = 6; // BreakDuration=5s + 1s buffer
            tracker.LogEvent("PHASE-2", $"Sleeping {breakWaitSeconds}s (BreakDuration=5s)…");
            await Task.Delay(TimeSpan.FromSeconds(breakWaitSeconds));

            // ── Phase 3: recover ──────────────────────────────────────────────
            fault.Disable();
            tracker.LogEvent("PHASE-3", "FaultSwitch OFF — sending probe requests");

            var phase3 = new List<object>();
            for (int i = 1; i <= 4; i++)
            {
                var (ok, body) = await client.GetAsync();
                var state = tracker.StateProvider.CircuitState.ToString();
                phase3.Add(new { req = i, ok, outcome = ok ? "SUCCESS" : body, circuit = state });
                tracker.LogEvent("PROBE", $"req={i} ok={ok} circuit={state}");
                await Task.Delay(200);
            }

            tracker.LogEvent("DONE",
                $"Final circuit: {tracker.StateProvider.CircuitState} ===");

            return Results.Ok(new
            {
                pipeline = "bulkhead(5) → circuit-breaker → retry(×2 exp-backoff) → timeout(2s)",
                phase1Results  = phase1,
                phase1bResults = phase1b,
                phase3Results  = phase3,
                finalCircuitState = tracker.StateProvider.CircuitState.ToString(),
                log = tracker.Log
            });
        }).WithTags("Resilience");

        return app;
    }
}
