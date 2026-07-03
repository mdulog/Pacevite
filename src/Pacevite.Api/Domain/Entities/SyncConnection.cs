using Pacevite.Api.Domain.Enums;

namespace Pacevite.Api.Domain.Entities;

public class SyncConnection
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string UserId { get; init; }
    public required SyncPlatform Platform { get; init; }
    public required string ExternalAthleteId { get; init; }

    // Encrypted at rest via IDataProtector — never store or log raw OAuth tokens (OWASP A02).
    public required string AccessTokenEncrypted { get; set; }
    public required string RefreshTokenEncrypted { get; set; }
    public required DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset ConnectedAt { get; init; } = DateTimeOffset.UtcNow;

    public ICollection<Event> Events { get; init; } = [];
}
