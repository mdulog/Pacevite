using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Pacevite.Api.Infrastructure.Sync;

// Talks to the real Strava API (https://developers.strava.com/docs). Registered via
// AddHttpClient — never construct with `new HttpClient()` (OWASP A05 / project convention).
public sealed class StravaClient(HttpClient httpClient, IOptions<StravaOptions> options) : IStravaClient
{
    private readonly StravaOptions _options = options.Value;

    public async Task<StravaTokenResult> ExchangeCodeAsync(string code, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["code"] = code,
            ["grant_type"] = "authorization_code"
        };

        return await PostTokenRequestAsync(form, ct);
    }

    public async Task<StravaTokenResult> RefreshTokenAsync(string refreshToken, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token"
        };

        return await PostTokenRequestAsync(form, ct);
    }

    public async Task<IReadOnlyList<StravaActivity>> GetRecentActivitiesAsync(string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/v3/athlete/activities?per_page=30");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var activities = await response.Content.ReadFromJsonAsync<List<StravaActivityDto>>(cancellationToken: ct)
            ?? [];

        return activities.Select(a => new StravaActivity
        {
            Id = a.Id,
            Name = a.Name,
            Type = a.Type,
            StartDate = a.StartDate,
            ElapsedTimeSecs = a.ElapsedTime
        }).ToList();
    }

    private async Task<StravaTokenResult> PostTokenRequestAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        using var response = await httpClient.PostAsync("oauth/token", new FormUrlEncodedContent(form), ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<StravaTokenDto>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Strava token response body was empty.");

        return new StravaTokenResult
        {
            AccessToken = body.AccessToken,
            RefreshToken = body.RefreshToken,
            ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(body.ExpiresAt),
            AthleteId = body.Athlete.Id.ToString()
        };
    }

    private sealed class StravaTokenDto
    {
        [JsonPropertyName("access_token")]
        public required string AccessToken { get; init; }

        [JsonPropertyName("refresh_token")]
        public required string RefreshToken { get; init; }

        [JsonPropertyName("expires_at")]
        public required long ExpiresAt { get; init; }

        [JsonPropertyName("athlete")]
        public required StravaAthleteDto Athlete { get; init; }
    }

    private sealed class StravaAthleteDto
    {
        [JsonPropertyName("id")]
        public required long Id { get; init; }
    }

    private sealed class StravaActivityDto
    {
        [JsonPropertyName("id")]
        public required long Id { get; init; }

        [JsonPropertyName("name")]
        public required string Name { get; init; }

        [JsonPropertyName("type")]
        public required string Type { get; init; }

        [JsonPropertyName("start_date")]
        public required DateTimeOffset StartDate { get; init; }

        [JsonPropertyName("elapsed_time")]
        public required int ElapsedTime { get; init; }
    }
}
