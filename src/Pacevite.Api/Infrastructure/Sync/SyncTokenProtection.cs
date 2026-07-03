namespace Pacevite.Api.Infrastructure.Sync;

// Shared IDataProtector purpose string for encrypting SyncConnection tokens at rest —
// centralized so every reader/writer derives the same protector (OWASP A02).
public static class SyncTokenProtection
{
    public const string Purpose = "Pacevite.StravaSync.Token";
}
