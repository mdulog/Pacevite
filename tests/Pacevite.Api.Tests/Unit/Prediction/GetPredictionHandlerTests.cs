using Microsoft.EntityFrameworkCore;
using Pacevite.Api.Domain.Entities;
using Pacevite.Api.Domain.Enums;
using Pacevite.Api.Features.Events.GetPrediction;
using Pacevite.Api.Infrastructure.Persistence;
using TUnit.Core;

namespace Pacevite.Api.Tests.Unit.Prediction;

[Category("Unit")]
public sealed class GetPredictionHandlerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly GetPredictionHandler _handler;
    private const string UserId = "user-predict-test";

    public GetPredictionHandlerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _handler = new GetPredictionHandler(_db);
    }

    public void Dispose() => _db.Dispose();

    private void SeedEvents(int count, int startSecs = 5000, int improvementPerEvent = 200)
    {
        for (int i = 0; i < count; i++)
        {
            _db.Events.Add(new Event
            {
                UserId = UserId,
                EventType = EventType.Hyrox,
                EventName = $"HYROX Event {i + 1}",
                EventDate = new DateOnly(2023, 1, 1).AddMonths(i * 6),
                Completion = CompletionStatus.Finished,
                ElapsedSecs = startSecs - i * improvementPerEvent,
            });
        }
        _db.SaveChanges();
    }

    [Test]
    public async Task Handle_TwoFinishedEvents_ReturnsPrediction()
    {
        // Arrange
        SeedEvents(2);
        var query = new GetPredictionQuery(UserId, "HYROX");

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.EventType).IsEqualTo("HYROX");
        await Assert.That(result.PredictedSecs).IsGreaterThan(0);
        await Assert.That(result.DataPoints.Count).IsEqualTo(3); // 2 historical + 1 projected
        await Assert.That(result.DataPoints.Last().EventId).IsNull();
        await Assert.That(result.DataPoints.Last().ElapsedSecs).IsNull();
    }

    [Test]
    public async Task Handle_ThreeEventsWithConsistentImprovement_ReturnsHighConfidence()
    {
        // Arrange
        SeedEvents(3);
        var query = new GetPredictionQuery(UserId, "HYROX");

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.ConfidenceLabel).IsEqualTo("High");
    }

    [Test]
    public async Task Handle_OneFinishedEvent_ReturnsNull()
    {
        // Arrange
        SeedEvents(1);
        var query = new GetPredictionQuery(UserId, "HYROX");

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Handle_ZeroFinishedEvents_ReturnsNull()
    {
        // Arrange
        _db.Events.Add(new Event
        {
            UserId = UserId,
            EventType = EventType.Hyrox,
            EventName = "HYROX DNF",
            EventDate = new DateOnly(2023, 1, 1),
            Completion = CompletionStatus.Dnf,
            ElapsedSecs = 9999,
        });
        _db.SaveChanges();
        var query = new GetPredictionQuery(UserId, "HYROX");

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Handle_AllEventsOnSameDate_ReturnsNull()
    {
        // Arrange — two events on the same date (zero X spread)
        for (int i = 0; i < 2; i++)
        {
            _db.Events.Add(new Event
            {
                UserId = UserId,
                EventType = EventType.Hyrox,
                EventName = $"HYROX {i}",
                EventDate = new DateOnly(2024, 6, 1),
                Completion = CompletionStatus.Finished,
                ElapsedSecs = 4800 - i * 100,
            });
        }
        _db.SaveChanges();
        var query = new GetPredictionQuery(UserId, "HYROX");

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Handle_UnknownEventType_ReturnsNull()
    {
        // Arrange
        SeedEvents(3);
        var query = new GetPredictionQuery(UserId, "UNKNOWN_TYPE");

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Handle_AvgImprovementSecs_IsCorrect()
    {
        // Arrange: 5000 → 4800 → 4600 (each 200s faster)
        SeedEvents(3, startSecs: 5000, improvementPerEvent: 200);
        var query = new GetPredictionQuery(UserId, "HYROX");

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.AvgImprovementSecs).IsEqualTo(200);
    }

    [Test]
    public async Task Handle_PredictionClampedToFloor_ReturnsLowConfidence()
    {
        // Arrange: extreme improvement trend that would predict below 3000s (Hyrox floor)
        for (int i = 0; i < 3; i++)
        {
            _db.Events.Add(new Event
            {
                UserId = UserId,
                EventType = EventType.Hyrox,
                EventName = $"HYROX {i}",
                EventDate = new DateOnly(2020, 1, 1).AddMonths(i),
                Completion = CompletionStatus.Finished,
                ElapsedSecs = 5000 - i * 1500, // absurdly steep slope
            });
        }
        _db.SaveChanges();
        var query = new GetPredictionQuery(UserId, "HYROX");

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.PredictedSecs).IsGreaterThanOrEqualTo(3000);
        await Assert.That(result.ConfidenceLabel).IsEqualTo("Low");
    }
}
