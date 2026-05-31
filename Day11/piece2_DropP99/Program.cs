using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Endpoints;
using QuotesApi.Extensions;
using QuotesApi.Middleware;
using QuotesApi.Models;
using Serilog;
using Serilog.Context;

var builder = WebApplication.CreateBuilder(args);

// Pull secrets from Azure Key Vault before any other configuration is read.
// In production the managed identity (or workload identity) authenticates
// automatically via DefaultAzureCredential — no credential in code or config.
// Locally, set AZURE_KEYVAULT_URI in user-secrets or launchSettings and log in
// with 'az login'. Key Vault secret names use '--' as a hierarchy separator
// (e.g. "ApplicationInsights--ConnectionString" → "ApplicationInsights:ConnectionString").
var keyVaultUri = builder.Configuration["AzureKeyVault:Uri"];

if (!string.IsNullOrEmpty(keyVaultUri))
{
    builder.Configuration.AddAzureKeyVault(
        new Uri(keyVaultUri),
        new DefaultAzureCredential());
}

builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddProblemDetails();

var app = builder.Build();

// Push ASP.NET Core's TraceIdentifier into Serilog's log context
// so every log line in a request carries the same TraceId property.
app.Use(async (ctx, next) =>
{
    using (LogContext.PushProperty("TraceId", ctx.TraceIdentifier))
        await next();
});

app.UseSerilogRequestLogging();

app.UseMiddleware<ExceptionMiddleware>();

app.UseAuthentication();

app.UseAuthorization();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    db.Database.Migrate();

    if (!db.Users.Any())
    {
        db.Users.Add(new User
        {
            Email = "test@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("password123")
        });

        db.SaveChanges();
    }

    // Seed 20 authors × 50 quotes = 1000 rows to make the N+1 on /api/quotes/by-author painful.
    // Each author fires its own SELECT, and Author has no index, so every query is a full scan.
    if (!db.Quotes.Any())
    {
        var now = DateTimeOffset.UtcNow;
        var quotes = new List<Quote>();
        for (var a = 1; a <= 20; a++)
        {
            var authorName = $"Author {a:D2}";
            for (var q = 1; q <= 50; q++)
            {
                var result = Quote.Create(
                    authorName,
                    $"Quote {q:D3} by {authorName} — the value of persistence is that it outlasts doubt.",
                    now);
                if (result.IsSuccess) quotes.Add(result.Value!);
            }
        }
        db.Quotes.AddRange(quotes);
        db.SaveChanges();
    }
}

app.MapAuthEndpoints();

app.MapQuoteEndpoints();

app.Run();

public partial class Program { }