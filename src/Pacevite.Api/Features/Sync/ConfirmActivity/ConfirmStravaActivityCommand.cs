using Mediator;
using Pacevite.Api.Contracts.Responses;

namespace Pacevite.Api.Features.Sync.ConfirmActivity;

public sealed record ConfirmStravaActivityCommand(
    string UserId,
    string ExternalActivityId,
    string EventName,
    DateOnly EventDate,
    int ElapsedSecs) : ICommand<EventResponse?>;
