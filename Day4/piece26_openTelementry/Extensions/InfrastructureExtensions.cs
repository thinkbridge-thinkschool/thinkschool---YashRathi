using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using QuotesApi.Abstractions;
using QuotesApi.Authorization;
using QuotesApi.Data;
using QuotesApi.Infrastructure;
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
            options.UseSqlite(connectionString));

        var otlpEndpoint = configuration["OpenTelemetry:OtlpEndpoint"] ?? "http://localhost:4317";

        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(Telemetry.ServiceName))
            .WithTracing(tracing => tracing
                .AddSource(Telemetry.ServiceName)
                .AddAspNetCoreInstrumentation()
                .AddEntityFrameworkCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint)));

        services.AddScoped<IQuoteRepository, QuoteRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddSingleton<IClock, SystemClock>();

        var jwtKey = configuration["Jwt:Key"]!;
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));

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
                    ValidIssuer = configuration["Jwt:Issuer"],
                    ValidAudience = configuration["Jwt:Audience"],
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

        return services;
    }
}
