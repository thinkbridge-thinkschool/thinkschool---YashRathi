using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using QuotesApi.Abstractions;
using QuotesApi.Data;

namespace Quotes.Tests.Integration;

/// <summary>
/// Boots the real ASP.NET Core pipeline in-memory against an isolated per-instance SQLite file.
/// Each test class creates one fixture → one fresh database.
/// ConfigureServices is used (not ConfigureAppConfiguration) so the DbContext override
/// runs after AddInfrastructure has registered the real one.
/// </summary>
internal sealed class IntegrationFixture : WebApplicationFactory<Program>
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"integration-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Swap the production SQLite DB for an isolated temp file.
            var dbDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (dbDescriptor is not null)
                services.Remove(dbDescriptor);

            services.AddDbContext<AppDbContext>(opts =>
                opts.UseSqlite($"Data Source={_dbPath}"));

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
        var config = Services.GetRequiredService<IConfiguration>();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var jwt = new JwtSecurityToken(
            issuer: config["Jwt:Issuer"],
            audience: config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { File.Delete(_dbPath); } catch { /* best-effort */ }
    }
}

internal sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow => new(2025, 6, 1, 12, 0, 0, TimeSpan.Zero);
}
