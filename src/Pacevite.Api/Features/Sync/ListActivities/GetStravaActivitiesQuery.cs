using Mediator;
using Pacevite.Api.Contracts.Responses;

namespace Pacevite.Api.Features.Sync.ListActivities;

public sealed record GetStravaActivitiesQuery(string UserId) : IQuery<IReadOnlyList<StravaActivityPreviewResponse>?>;
