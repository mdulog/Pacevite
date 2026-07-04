namespace Pacevite.Api.Contracts.Responses;

// Splitless list item — splits are only served by GET /api/events/{id}.
public sealed record EventSummaryResponse(
    Guid Id,
    string EventType,
    string EventName,
    DateOnly EventDate,
    string Completion,
    int ElapsedSecs,
    int? OverallRank,
    int? AgeGroupRank,
    int? FieldSize,
    int? AgeGroupFieldSize,
    string Source,
    bool NeedsEnrichment,
    DateTimeOffset CreatedAt);

public sealed record PagedEventsResponse(
    IReadOnlyList<EventSummaryResponse> Items,
    string? NextCursor);
