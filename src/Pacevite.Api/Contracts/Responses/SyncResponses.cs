namespace Pacevite.Api.Contracts.Responses;

public sealed record ConnectStravaResponse(string AuthorizeUrl);

public sealed record SyncConnectionResponse(string Platform, DateTimeOffset ConnectedAt);

public sealed record StravaActivityPreviewResponse(
    string ExternalActivityId,
    string Name,
    DateOnly EventDate,
    int ElapsedSecs,
    bool PossibleDuplicate);
