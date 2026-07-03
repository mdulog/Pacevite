namespace Pacevite.Api.Infrastructure.Sync;

// Boundary abstraction over the Strava API (https://developers.strava.com/docs) —
// mock this interface in tests rather than the underlying HttpClient (OWASP-adjacent
// convention: mock only external boundaries, per project testing standards).
public interface IStravaClient
{
    Task<StravaTokenResult> ExchangeCodeAsync(string code, CancellationToken ct);
    Task<StravaTokenResult> RefreshTokenAsync(string refreshToken, CancellationToken ct);
    Task<IReadOnlyList<StravaActivity>> GetRecentActivitiesAsync(string accessToken, CancellationToken ct);
}

public sealed class StravaTokenResult
{
    public required string AccessToken { get; init; }
    public required string RefreshToken { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public required string AthleteId { get; init; }
}

// Mirrors Strava's SummaryActivity fields we use (developers.strava.com/docs/reference —
// "type": "Run" etc., "start_date": UTC ISO 8601, "elapsed_time": seconds).
public sealed class StravaActivity
{
    public required long Id { get; init; }
    public required string Name { get; init; }
    public required string Type { get; init; }
    public required DateTimeOffset StartDate { get; init; }
    public required int ElapsedTimeSecs { get; init; }
}
