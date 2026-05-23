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

builder.Services.AddHealthChecks();

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
}

app.MapHealthChecks("/health");

app.MapAuthEndpoints();

app.MapQuoteEndpoints();

app.Run();

public partial class Program { }