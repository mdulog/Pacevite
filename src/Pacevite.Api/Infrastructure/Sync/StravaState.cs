using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace Pacevite.Api.Infrastructure.Sync;

// Signs the OAuth `state` parameter so the callback can recover which user initiated
// the flow without a server-side session store, and rejects forged or stale values
// (OWASP A01/A07 — state cannot be forged without the app's Data Protection keys).
public static class StravaState
{
    private const string Purpose = "Pacevite.StravaSync.State";
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    public static string Create(IDataProtectionProvider provider, string userId, TimeProvider? timeProvider = null)
    {
        var protector = provider.CreateProtector(Purpose);
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        var expiresAt = now.Add(Lifetime).ToUnixTimeSeconds();
        return protector.Protect($"{userId}|{expiresAt}");
    }

    // Returns null for a missing, forged, tampered, or expired state — callers should
    // treat that as an invalid callback rather than throwing.
    public static string? TryUnprotect(IDataProtectionProvider provider, string state, TimeProvider? timeProvider = null)
    {
        var protector = provider.CreateProtector(Purpose);

        string payload;
        try
        {
            payload = protector.Unprotect(state);
        }
        catch (CryptographicException)
        {
            return null;
        }

        var parts = payload.Split('|', 2);
        if (parts.Length != 2 || !long.TryParse(parts[1], out var expiresAtUnix))
            return null;

        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(expiresAtUnix);
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        return expiresAt >= now ? parts[0] : null;
    }
}
