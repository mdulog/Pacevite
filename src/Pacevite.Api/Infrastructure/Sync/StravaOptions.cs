namespace Pacevite.Api.Infrastructure.Sync;

public sealed class StravaOptions
{
    public const string SectionName = "Strava";

    public required string ClientId { get; init; }
    public required string ClientSecret { get; init; }
    public required string RedirectUri { get; init; }
}
