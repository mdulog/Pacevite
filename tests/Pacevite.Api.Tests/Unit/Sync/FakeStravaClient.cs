using Pacevite.Api.Infrastructure.Sync;

namespace Pacevite.Api.Tests.Unit.Sync;

// Hand-written test double for the external Strava boundary — the interface is small
// enough that a fake is clearer than a mocking framework here.
public sealed class FakeStravaClient : IStravaClient
{
    public StravaTokenResult TokenToReturn { get; set; } = new()
    {
        AccessToken = "fake-access-token",
        RefreshToken = "fake-refresh-token",
        ExpiresAt = DateTimeOffset.UtcNow.AddHours(6),
        AthleteId = "athlete-1"
    };

    public List<StravaActivity> ActivitiesToReturn { get; set; } = [];

    public string? LastExchangedCode { get; private set; }
    public string? LastRefreshedToken { get; private set; }
    public string? LastAccessTokenUsedForActivities { get; private set; }
    public int RefreshCallCount { get; private set; }

    public Task<StravaTokenResult> ExchangeCodeAsync(string code, CancellationToken ct)
    {
        LastExchangedCode = code;
        return Task.FromResult(TokenToReturn);
    }

    public Task<StravaTokenResult> RefreshTokenAsync(string refreshToken, CancellationToken ct)
    {
        LastRefreshedToken = refreshToken;
        RefreshCallCount++;
        return Task.FromResult(TokenToReturn);
    }

    public Task<IReadOnlyList<StravaActivity>> GetRecentActivitiesAsync(string accessToken, CancellationToken ct)
    {
        LastAccessTokenUsedForActivities = accessToken;
        return Task.FromResult<IReadOnlyList<StravaActivity>>(ActivitiesToReturn);
    }
}
