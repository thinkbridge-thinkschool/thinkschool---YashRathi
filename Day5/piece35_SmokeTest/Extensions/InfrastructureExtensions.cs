using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Polly;
using QuotesApi.Abstractions;
using QuotesApi.Authorization;
using QuotesApi.Data;
using QuotesApi.Infrastructure;
using QuotesApi.Options;
using QuotesApi.Repositories;
using QuotesApi.Services;

namespace QuotesApi.Extensions;

public static class InfrastructureExtensions
{
    private const string InternalScheme = "InternalJwt";
    private const string EntraScheme = "EntraId";
    private const string MultiScheme = "MultiScheme";

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? "Data Source=quotes.db";

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite(connectionString));

        var otlpEndpoint = configuration["OpenTelemetry:OtlpEndpoint"] ?? "http://localhost:4317";
        var appInsightsConnectionString = configuration["ApplicationInsights:ConnectionString"];

        var otelBuilder = services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(Telemetry.ServiceName))
            .WithTracing(tracing => tracing
                .AddSource(Telemetry.ServiceName)
                .AddAspNetCoreInstrumentation()
                .AddEntityFrameworkCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint)))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation());

        // When a connection string is present (sourced from Key Vault in prod),
        // export logs + metrics + traces to Azure Application Insights as well.
        // UseAzureMonitor adds its own exporters on top of the existing OTLP ones.
        if (!string.IsNullOrEmpty(appInsightsConnectionString))
        {
            otelBuilder.UseAzureMonitor(options =>
                options.ConnectionString = appInsightsConnectionString);
        }

        services.AddScoped<IQuoteRepository, QuoteRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddSingleton<IClock, SystemClock>();

        // Bind the Jwt section to a typed record; IOptions<JwtOptions> is
        // injectable in any service that needs JWT configuration.
        services.Configure<JwtOptions>(configuration.GetSection("Jwt"));

        var jwtOpts = configuration.GetSection("Jwt").Get<JwtOptions>()!;
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOpts.Key));

        var tenantId = configuration["EntraId:TenantId"]!;
        var instance = configuration["EntraId:Instance"] ?? "https://login.microsoftonline.com/";
        var entraAudience = configuration["EntraId:Audience"]!;

        services
            .AddAuthentication(MultiScheme)
            // Route to the right scheme by peeking at the 'iss' claim before validation.
            .AddPolicyScheme(MultiScheme, "Internal or Entra JWT", options =>
            {
                options.ForwardDefaultSelector = context =>
                {
                    var authHeader = context.Request.Headers[HeaderNames.Authorization]
                        .FirstOrDefault();

                    if (authHeader?.StartsWith("Bearer ") == true)
                    {
                        var token = authHeader["Bearer ".Length..].Trim();
                        var handler = new JwtSecurityTokenHandler();
                        if (handler.CanReadToken(token))
                        {
                            var jwt = handler.ReadJwtToken(token);
                            if (jwt.Issuer.Contains("login.microsoftonline.com",
                                    StringComparison.OrdinalIgnoreCase))
                                return EntraScheme;
                        }
                    }

                    return InternalScheme;
                };
            })
            // Internal callers use our HMAC-signed JWT.
            .AddJwtBearer(InternalScheme, options =>
            {
                // Keep claim names as they appear in the JWT ("sub", "scope", etc.)
                // so code can use JwtRegisteredClaimNames constants consistently.
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtOpts.Issuer,
                    ValidAudience = jwtOpts.Audience,
                    IssuerSigningKey = signingKey,
                    ClockSkew = TimeSpan.Zero
                };
            })
            // SPA / customer-facing callers use Entra ID (Azure AD) tokens.
            // Authority causes the middleware to auto-fetch signing keys from
            // https://login.microsoftonline.com/{tenant}/v2.0/.well-known/openid-configuration
            .AddJwtBearer(EntraScheme, options =>
            {
                options.Authority = $"{instance}{tenantId}/v2.0";
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateAudience = true,
                    ValidAudience = entraAudience,
                    ValidateIssuer = true,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };
            });

        services.AddAuthorization(options =>
        {
            // Policy 1 — claim-based: caller must carry scope = quotes.write
            options.AddPolicy("can-edit-quotes", p =>
                p.RequireClaim("scope", "quotes.write"));

            // Policy 2 — custom requirement: authenticated user must own the resource.
            // The actual ownership check is done resource-by-resource inside the endpoint
            // via IAuthorizationService.AuthorizeAsync(user, quote, "can-delete-own-quote").
            options.AddPolicy("can-delete-own-quote", p =>
                p.RequireAuthenticatedUser()
                 .AddRequirements(new OwnerRequirement()));
        });

        // Register the resource-based handler so DI can resolve it.
        services.AddSingleton<IAuthorizationHandler, QuoteOwnerAuthorizationHandler>();

        // ── Resilient HTTP client for the external quote-tagging service ─────────
        // Handler chain (outermost → innermost):
        //   ResiliencePipeline → (primary HttpClientHandler / test stub)
        //
        // Pipeline order matters: retry wraps circuit breaker wraps timeout.
        // A timed-out attempt counts as a failure toward the circuit breaker.
        services.AddHttpClient(ExternalQuoteService.ClientName, client =>
        {
            client.BaseAddress = new Uri("https://api.quotetags.example.com");
            // Disable the HttpClient-level timeout; Polly owns total-request timing.
            client.Timeout = Timeout.InfiniteTimeSpan;
        })
        .AddResilienceHandler("default", (ResiliencePipelineBuilder<HttpResponseMessage> builder,
                                          ResilienceHandlerContext ctx) =>
        {
            var logger = ctx.ServiceProvider
                .GetRequiredService<ILogger<ExternalQuoteService>>();

            // 1. Retry: 3 attempts, exponential + jitter, log every retry.
            builder.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                // Base delay kept short for demo; use ≥1 s in real production.
                Delay = TimeSpan.FromMilliseconds(200),
                OnRetry = args =>
                {
                    logger.LogWarning(
                        "HTTP retry {Attempt}/{Max} after {Delay}ms — " +
                        "{Method} {Url} responded {Reason}",
                        args.AttemptNumber + 1,
                        3,
                        args.RetryDelay.TotalMilliseconds,
                        args.Outcome.Result?.RequestMessage?.Method,
                        args.Outcome.Result?.RequestMessage?.RequestUri,
                        args.Outcome.Exception?.Message
                            ?? args.Outcome.Result?.ReasonPhrase);
                    return ValueTask.CompletedTask;
                }
            });

            // 2. Circuit breaker: open after ≥50 % failures over a 30-second window
            //    (min 3 requests needed before tripping).
            builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                SamplingDuration = TimeSpan.FromSeconds(30),
                FailureRatio = 0.5,
                MinimumThroughput = 3,
                BreakDuration = TimeSpan.FromSeconds(10)
            });

            // 3. Total timeout per attempt (after retries have been exhausted the
            //    outer timeout still applies to the last attempt).
            builder.AddTimeout(TimeSpan.FromSeconds(10));
        });

        services.AddTransient<IExternalQuoteService, ExternalQuoteService>();

        return services;
    }
}
