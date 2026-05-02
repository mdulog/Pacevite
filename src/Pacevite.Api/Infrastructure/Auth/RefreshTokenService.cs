using Microsoft.EntityFrameworkCore;
using Pacevite.Api.Domain.Entities;
using Pacevite.Api.Infrastructure.Persistence;

namespace Pacevite.Api.Infrastructure.Auth;

public interface IRefreshTokenService
{
    Task<string> CreateAsync(string userId, CancellationToken ct = default);
    Task<(bool Valid, string? UserId, string? NewRawToken)> RotateAsync(string rawToken, CancellationToken ct = default);
    Task RevokeAsync(string rawToken, CancellationToken ct = default);
}

public class RefreshTokenService(
    AppDbContext db,
    IJwtTokenService jwtTokenService,
    ILogger<RefreshTokenService> logger) : IRefreshTokenService
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromDays(7);

    public async Task<string> CreateAsync(string userId, CancellationToken ct = default)
    {
        var rawToken = jwtTokenService.GenerateRefreshToken();
        var tokenHash = jwtTokenService.HashToken(rawToken);

        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = userId,
            TokenHash = tokenHash,
            ExpiresAt = DateTimeOffset.UtcNow.Add(TokenLifetime)
        });

        await db.SaveChangesAsync(ct);
        return rawToken;
    }

    public async Task<(bool Valid, string? UserId, string? NewRawToken)> RotateAsync(
        string rawToken,
        CancellationToken ct = default)
    {
        var tokenHash = jwtTokenService.HashToken(rawToken);
        var existing = await db.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash, ct);

        if (existing is null || !existing.IsActive)
        {
            logger.LogWarning("Refresh token rotation rejected: token not found or inactive");
            return (false, null, null);
        }

        var newRawToken = jwtTokenService.GenerateRefreshToken();
        var newTokenHash = jwtTokenService.HashToken(newRawToken);

        existing.RevokedAt = DateTimeOffset.UtcNow;
        existing.ReplacedByTokenHash = newTokenHash;

        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = existing.UserId,
            TokenHash = newTokenHash,
            ExpiresAt = DateTimeOffset.UtcNow.Add(TokenLifetime)
        });

        await db.SaveChangesAsync(ct);
        return (true, existing.UserId, newRawToken);
    }

    public async Task RevokeAsync(string rawToken, CancellationToken ct = default)
    {
        var tokenHash = jwtTokenService.HashToken(rawToken);
        var existing = await db.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash && rt.RevokedAt == null, ct);

        if (existing is null)
            return;

        existing.RevokedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
