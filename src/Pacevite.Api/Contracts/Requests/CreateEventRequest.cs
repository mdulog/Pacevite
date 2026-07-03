namespace Pacevite.Api.Contracts.Requests;

public sealed record CreateEventRequest(
    string EventType,
    string EventName,
    DateOnly EventDate,
    string Completion,
    int ElapsedSecs,
    int? OverallRank,
    int? AgeGroupRank,
    int? FieldSize,
    int? AgeGroupFieldSize,
    IReadOnlyList<CreateEventSplitRequest>? Splits);

public sealed record CreateEventSplitRequest(
    string SplitType,
    string SplitLabel,
    int SplitSecs,
    int CumulativeSecs);
