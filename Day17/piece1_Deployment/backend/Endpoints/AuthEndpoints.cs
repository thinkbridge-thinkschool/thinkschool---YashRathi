using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using QuotesApi.Abstractions;
using QuotesApi.Data;
using QuotesApi.Dtos;
using QuotesApi.Models;
using QuotesApi.Options;
using QuotesApi.Repositories;

namespace QuotesApi.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        // ── LOGIN ────────────────────────────────────────────────────────────
        app.MapPost("/api/auth/login", async (
            LoginRequest request,
            AppDbContext db,
            IRefreshTokenRepository tokenRepo,
            IOptions<JwtOptions> jwtOptions,
            IClock clock,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            logger.LogInformation("Login attempt for email {Email}", request.Email);

            var user = await db.Users
                .FirstOrDefaultAsync(u => u.Email == request.Email, ct);

            if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            {
                logger.LogWarning("Login failed for email {Email}: invalid credentials", request.Email);
                return Results.Unauthorized();
            }

            var rawRefresh = GenerateRawToken();

            await tokenRepo.AddAsync(new RefreshToken
            {
                TokenHash = HashToken(rawRefresh),
                UserId = user.Id,
                ExpiresAt = clock.UtcNow.AddDays(7),
                FamilyId = Guid.NewGuid().ToString("N")
            }, ct);

            logger.LogInformation(
                "Login successful for user {UserId} email {Email}", user.Id, user.Email);

            var opts = jwtOptions.Value;
            return Results.Ok(new LoginResponse(
                BuildAccessToken(user, opts),
                rawRefresh,
                (int)opts.AccessTokenLifetime.TotalSeconds));
        });

        // ── REFRESH ──────────────────────────────────────────────────────────
        app.MapPost("/api/auth/refresh", async (
            RefreshRequest request,
            AppDbContext db,
            IRefreshTokenRepository tokenRepo,
            IOptions<JwtOptions> jwtOptions,
            IClock clock,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            var now = clock.UtcNow;
            var stored = await tokenRepo.FindByHashAsync(HashToken(request.RefreshToken), ct);

            if (stored is null)
                return Results.Unauthorized();

            // ── REUSE DETECTION ──────────────────────────────────────────────
            // A revoked token that is presented again means the raw value leaked.
            // Revoke every token in the family and force re-auth.
            if (stored.IsRevoked)
            {
                logger.LogWarning(
                    "SECURITY: Refresh token reuse detected. " +
                    "Family={FamilyId} User={UserId}. Revoking entire chain.",
                    stored.FamilyId, stored.UserId);

                await tokenRepo.RevokeFamilyAsync(stored.FamilyId, now, ct);
                return Results.Unauthorized();
            }

            if (stored.IsExpired(now))
                return Results.Unauthorized();

            var user = await db.Users.FindAsync([stored.UserId], ct);
            if (user is null)
                return Results.Unauthorized();

            // ── ROTATE ───────────────────────────────────────────────────────
            var rawNewRefresh = GenerateRawToken();
            var newHash = HashToken(rawNewRefresh);

            // Mark old token consumed (single-use)
            stored.RevokedAt = now;
            stored.ReplacedByToken = newHash;

            // Mint new token in the same family
            db.RefreshTokens.Add(new RefreshToken
            {
                TokenHash = newHash,
                UserId = user.Id,
                ExpiresAt = now.AddDays(7),
                FamilyId = stored.FamilyId
            });

            await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "Refresh token rotated for user {UserId} family {FamilyId}",
                user.Id, stored.FamilyId);

            var opts = jwtOptions.Value;
            return Results.Ok(new LoginResponse(
                BuildAccessToken(user, opts),
                rawNewRefresh,
                (int)opts.AccessTokenLifetime.TotalSeconds));
        });

        // ── LOGOUT ───────────────────────────────────────────────────────────
        app.MapPost("/api/auth/logout", async (
            RefreshRequest request,
            IRefreshTokenRepository tokenRepo,
            IClock clock,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            var stored = await tokenRepo.FindByHashAsync(HashToken(request.RefreshToken), ct);

            if (stored is not null && !stored.IsRevoked)
            {
                stored.RevokedAt = clock.UtcNow;
                await tokenRepo.SaveChangesAsync(ct);
                logger.LogInformation(
                    "Refresh token revoked for user {UserId}", stored.UserId);
            }

            return Results.NoContent();
        });

        return app;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string HashToken(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

    private static string GenerateRawToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    private static string BuildAccessToken(User user, JwtOptions opts)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opts.Key));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var jwt = new JwtSecurityToken(
            issuer: opts.Issuer,
            audience: opts.Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim("scope", "quotes.write")
            ],
            expires: DateTime.UtcNow.Add(opts.AccessTokenLifetime),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }
}
