using Mediator;
using Pacevite.Api.Contracts.Responses;

namespace Pacevite.Api.Features.Events.GetEvents;

public sealed record GetEventsQuery(
    string UserId,
    string? EventType = null,
    DateOnly? From = null,
    DateOnly? To = null,
    string? Search = null,
    string? Cursor = null,
    int Limit = GetEventsQuery.DefaultLimit) : IQuery<PagedEventsResponse>
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 100;
    public const int MaxSearchLength = 100;
}
