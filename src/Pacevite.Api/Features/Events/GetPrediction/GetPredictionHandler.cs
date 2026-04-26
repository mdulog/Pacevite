using Mediator;
using Microsoft.EntityFrameworkCore;
using Pacevite.Api.Contracts.Responses;
using Pacevite.Api.Domain.Enums;
using Pacevite.Api.Infrastructure.Persistence;
using Pacevite.Api.Infrastructure.Regression;

namespace Pacevite.Api.Features.Events.GetPrediction;

public sealed class GetPredictionHandler(AppDbContext db)
    : IQueryHandler<GetPredictionQuery, PredictionResponse?>
{
    private static readonly Dictionary<EventType, int> FloorSecs = new()
    {
        [EventType.Hyrox]    = 3000,
        [EventType.Marathon] = 7200,
        [EventType.Spartan]  = 1800,
        [EventType.Generic]  = 60,
    };

    public async ValueTask<PredictionResponse?> Handle(
        GetPredictionQuery query, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<EventType>(query.EventType, ignoreCase: true, out var eventType))
            return null;

        var events = await db.Events
            .Where(e => e.UserId == query.UserId
                     && e.EventType == eventType
                     && e.Completion == CompletionStatus.Finished)
            .OrderBy(e => e.EventDate)
            .ToListAsync(cancellationToken);

        if (events.Count < 2)
            return null;

        var firstDate = events[0].EventDate.ToDateTime(TimeOnly.MinValue);

        var regressionPoints = events
            .Select(e => (
                (double)(e.EventDate.ToDateTime(TimeOnly.MinValue) - firstDate).TotalDays,
                (double)e.ElapsedSecs))
            .ToList();

        if (regressionPoints.All(p => p.Item1 == 0))
            return null;

        var regression    = LinearRegression.Fit(regressionPoints);
        var todayDays     = (DateTime.UtcNow.Date - firstDate).TotalDays;
        var rawPredicted  = LinearRegression.Predict(regression, todayDays);

        int floor     = FloorSecs[eventType];
        bool clamped  = rawPredicted < floor;
        int predictedSecs = (int)Math.Max(rawPredicted, floor);

        var confidence     = DeriveConfidence(regression.RSquared, events.Count, clamped);
        int avgImprovement = ComputeAvgImprovement(events.Select(e => e.ElapsedSecs).ToList());

        var dataPoints = events
            .Select(e =>
            {
                var days   = (e.EventDate.ToDateTime(TimeOnly.MinValue) - firstDate).TotalDays;
                var fitted = (int)LinearRegression.Predict(regression, days);
                return new PredictionDataPoint(e.Id, e.EventDate, e.ElapsedSecs, fitted);
            })
            .Append(new PredictionDataPoint(
                null,
                DateOnly.FromDateTime(DateTime.UtcNow),
                null,
                predictedSecs))
            .ToList();

        return new PredictionResponse(
            query.EventType.ToUpperInvariant(),
            predictedSecs,
            confidence,
            avgImprovement,
            dataPoints);
    }

    private static string DeriveConfidence(double rSquared, int count, bool clamped)
    {
        if (clamped) return "Low";
        if (rSquared >= 0.85 && count >= 3) return "High";
        if (rSquared >= 0.60 || count == 2) return "Medium";
        return "Low";
    }

    private static int ComputeAvgImprovement(IReadOnlyList<int> elapsed)
    {
        if (elapsed.Count < 2) return 0;
        var deltas = Enumerable.Range(1, elapsed.Count - 1)
            .Select(i => elapsed[i - 1] - elapsed[i]);
        return (int)deltas.Average();
    }
}
