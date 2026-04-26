namespace Pacevite.Api.Contracts.Responses;

public sealed record PredictionResponse(
    string EventType,
    int PredictedSecs,
    string ConfidenceLabel,
    int AvgImprovementSecs,
    IReadOnlyList<PredictionDataPoint> DataPoints);

public sealed record PredictionDataPoint(
    Guid? EventId,
    DateOnly EventDate,
    int? ElapsedSecs,
    int FittedSecs);
