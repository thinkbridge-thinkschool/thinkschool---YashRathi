using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using QuotesApi.Abstractions;
using QuotesApi.Data;
using QuotesApi.Options;

namespace Quotes.Tests.Integration;

/// <summary>
/// Boots the ASP.NET Core pipeline in-memory against an isolated SQL Server database
/// created on the shared Testcontainers instance. Each test class gets its own database
/// (integration_{guid}) so tests are fully isolated without paying per-class container
/// startup cost.
///
/// ConfigureServices is used (not ConfigureAppConfiguration) so the DbContext override
/// runs after AddInfrastructure has registered the real one.
/// </summary>
internal sealed class IntegrationFixture : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public IntegrationFixture(SqlServerFixture server)
    {
        // Give each fixture instance its own database on the shared SQL Server container.
        var builder = new SqlConnectionStringBuilder(server.ConnectionString)
        {
            InitialCatalog = $"integration_{Guid.NewGuid():N}"
        };
        _connectionString = builder.ConnectionString;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        // Jwt__Key maps to Jwt:Key via ASP.NET Core's env-var convention.
        // Must be set before the host builds so AddInfrastructure can read it.
        Environment.SetEnvironmentVariable("Jwt__Key",
            "super-secret-jwt-signing-key-must-be-32-bytes-min!!");
        builder.ConfigureServices(services =>
        {
            // EF Core 8+ registers IDbContextOptionsConfiguration<T> (the config delegate)
            // rather than DbContextOptions<T> directly. Removing only DbContextOptions<T>
            // leaves the SQLite config delegate behind, causing a dual-provider conflict.
            // Remove both to cleanly replace SQLite with SQL Server.
            var toRemove = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>)
                         || d.ServiceType == typeof(IDbContextOptionsConfiguration<AppDbContext>))
                .ToList();
            foreach (var d in toRemove)
                services.Remove(d);

            services.AddDbContext<AppDbContext>(opts =>
                opts.UseSqlServer(_connectionString)
                    .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

            // Freeze time so token expiry is predictable in tests.
            var clockDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(IClock));
            if (clockDescriptor is not null)
                services.Remove(clockDescriptor);

            services.AddSingleton<IClock>(new FakeClock());
        });
    }

    /// <summary>
    /// Builds a signed JWT using the app's own key but with caller-supplied claims.
    /// Useful for crafting tokens that deliberately omit or alter specific claims.
    /// </summary>
    internal string BuildToken(IEnumerable<Claim> claims)
    {
        var opts = Services.GetRequiredService<IOptions<JwtOptions>>().Value;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opts.Key));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var jwt = new JwtSecurityToken(
            issuer: opts.Issuer,
            audience: opts.Audience,
            claims: claims,
            expires: DateTime.UtcNow.Add(opts.AccessTokenLifetime),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    public override async ValueTask DisposeAsync()
    {
        // Drop the per-test-class database before releasing the factory so the container
        // stays clean. Best-effort: if the host is already gone, skip silently.
        try
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureDeletedAsync();
        }
        catch { }

        await base.DisposeAsync();
    }
}

internal sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow => new(2025, 6, 1, 12, 0, 0, TimeSpan.Zero);
}
