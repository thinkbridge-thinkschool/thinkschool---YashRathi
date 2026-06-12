using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using QuotesApi.Abstractions;
using QuotesApi.Authorization;
using QuotesApi.BackgroundJobs;
using QuotesApi.Cache;
using QuotesApi.Commands;
using QuotesApi.Data;
using QuotesApi.Infrastructure;
using QuotesApi.Options;
using QuotesApi.Queries;
using QuotesApi.Outbox;
using QuotesApi.Repositories;

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
            options.UseSqlServer(connectionString));

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

        services.AddMemoryCache();

        // Redis as L2 distributed cache.
        // Skipped when no connection string is set — HybridCache then runs L1-only,
        // which still provides stampede protection. Integration tests take this path.
        var redisConn = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrEmpty(redisConn))
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConn;
                options.InstanceName = "QuotesApi:";
            });
        }

        // HybridCache = L1 in-memory + L2 Redis (when available) + built-in stampede protection.
        // Stampede protection works even in L1-only mode: only ONE factory call executes per key
        // regardless of how many concurrent requests arrive for the same cold entry.
#pragma warning disable EXTEXP0018
        services.AddHybridCache(options =>
        {
            options.DefaultEntryOptions = new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(5),        // L2 (Redis) TTL
                LocalCacheExpiration = TimeSpan.FromSeconds(30) // L1 (in-memory) TTL
            };
        });
#pragma warning restore EXTEXP0018

        services.AddSingleton<CacheMetrics>();

        services.AddScoped<IQuoteRepository, QuoteRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddSingleton<IClock, SystemClock>();

        // CQRS-lite handlers — scoped to match DbContext lifetime.
        services.AddScoped<CreateQuoteCommandHandler>();
        services.AddScoped<GetQuotesQueryHandler>();
        services.AddScoped<GetQuoteByIdQueryHandler>();
        services.AddScoped<GetQuotesDapperQueryHandler>();

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

        // Background jobs — one singleton acts as channel writer (IEmailOutbox)
        // and hosted worker (drains the channel off the request thread).
        services.AddSingleton<EmailOutboxWorker>();
        services.AddSingleton<IEmailOutbox>(sp => sp.GetRequiredService<EmailOutboxWorker>());
        services.AddHostedService(sp => sp.GetRequiredService<EmailOutboxWorker>());

        // IHostedService contrast: timer-driven, no queue.
        services.AddHostedService<DailyReportHostedService>();

        // Transactional outbox relay: polls DB for pending OutboxMessages and publishes them.
        // IMessagePublisher is scoped so it can be swapped (e.g. Azure Service Bus) per environment.
        services.AddScoped<IMessagePublisher, LoggingMessagePublisher>();
        services.AddHostedService<OutboxRelayWorker>();

        // OpenAPI document — Scalar UI at /scalar/v1.
        // To test auth endpoints: call POST /api/auth/login → copy the token →
        // click the lock icon in Scalar and paste it as a Bearer token.
        services.AddOpenApi();

        return services;
    }
}
