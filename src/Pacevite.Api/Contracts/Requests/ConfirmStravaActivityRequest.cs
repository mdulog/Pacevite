namespace Pacevite.Api.Contracts.Requests;

// Carries the activity fields the client already fetched via the preview list —
// confirming does not re-fetch from Strava, avoiding a second external round-trip.
public sealed record ConfirmStravaActivityRequest(
    string ExternalActivityId,
    string Name,
    DateOnly EventDate,
    int ElapsedSecs);
