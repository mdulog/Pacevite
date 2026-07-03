using Mediator;
using Pacevite.Api.Contracts.Responses;

namespace Pacevite.Api.Features.Events.CreateEvent;

public sealed record CreateEventCommand(
    string UserId,
    string EventType,
    string EventName,
    DateOnly EventDate,
    string Completion,
    int ElapsedSecs,
    int? OverallRank,
    int? AgeGroupRank,
    int? FieldSize,
    int? AgeGroupFieldSize,
    IReadOnlyList<CreateEventSplitInput> Splits) : ICommand<EventResponse?>;

public sealed record CreateEventSplitInput(
    string SplitType,
    string SplitLabel,
    int SplitSecs,
    int CumulativeSecs);
