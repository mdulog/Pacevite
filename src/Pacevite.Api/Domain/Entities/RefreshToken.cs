namespace Pacevite.Api.Domain.Entities;

public class RefreshToken
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string UserId { get; init; }
    public required string TokenHash { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RevokedAt { get; set; }
    public string? ReplacedByTokenHash { get; set; }

    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;
    public bool IsActive => RevokedAt is null && !IsExpired;
}
