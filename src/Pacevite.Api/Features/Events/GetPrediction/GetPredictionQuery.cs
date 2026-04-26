using Mediator;
using Pacevite.Api.Contracts.Responses;

namespace Pacevite.Api.Features.Events.GetPrediction;

public sealed record GetPredictionQuery(string UserId, string EventType)
    : IQuery<PredictionResponse?>;
