using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Pacevite.Api.Domain.Entities;
using Pacevite.Api.Domain.Enums;
using Pacevite.Api.Features.Sync.ConfirmActivity;
using Pacevite.Api.Infrastructure.Persistence;
using TUnit.Core;

namespace Pacevite.Api.Tests.Unit.Sync;

[Category("Unit")]
public sealed class ConfirmStravaActivityHandlerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ConfirmStravaActivityHandler _handler;
    private const string UserId = "user-confirm-strava-test";

    public ConfirmStravaActivityHandlerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _handler = new ConfirmStravaActivityHandler(_db, NullLogger<ConfirmStravaActivityHandler>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private SyncConnection SeedConnection()
    {
        var connection = new SyncConnection
        {
            UserId = UserId,
            Platform = SyncPlatform.Strava,
            ExternalAthleteId = "athlete-1",
            AccessTokenEncrypted = "encrypted-access",
            RefreshTokenEncrypted = "encrypted-refresh",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(6)
        };
        _db.SyncConnections.Add(connection);
        _db.SaveChanges();
        return connection;
    }

    [Test]
    public async Task Handle_NewActivity_CreatesEventFlaggedForEnrichment()
    {
        // Arrange
        SeedConnection();
        var command = new ConfirmStravaActivityCommand(UserId, "strava-activity-1", "Sunday Long Run", new DateOnly(2026, 5, 3), 5400);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.EventType).IsEqualTo("GENERIC");
        await Assert.That(result.Completion).IsEqualTo("FINISHED");
        await Assert.That(result.EventName).IsEqualTo("Sunday Long Run");
        await Assert.That(result.ElapsedSecs).IsEqualTo(5400);
        await Assert.That(result.Source).IsEqualTo("STRAVA");
        await Assert.That(result.NeedsEnrichment).IsTrue();
        await Assert.That(result.OverallRank).IsNull();
    }

    [Test]
    public async Task Handle_NewActivity_PersistsExternalActivityIdAndSyncConnectionId()
    {
        // Arrange
        var connection = SeedConnection();
        var command = new ConfirmStravaActivityCommand(UserId, "strava-activity-2", "Track Session", new DateOnly(2026, 5, 4), 3600);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        var saved = await _db.Events.SingleAsync(e => e.Id == result!.Id);
        await Assert.That(saved.ExternalActivityId).IsEqualTo("strava-activity-2");
        await Assert.That(saved.SyncConnectionId).IsEqualTo(connection.Id);
    }

    [Test]
    public async Task Handle_ActivityAlreadyImported_ReturnsNullAndDoesNotDuplicate()
    {
        // Arrange
        SeedConnection();
        var command = new ConfirmStravaActivityCommand(UserId, "strava-activity-3", "Race Day", new DateOnly(2026, 5, 5), 4200);
        await _handler.Handle(command, CancellationToken.None);

        // Act — confirm the same activity a second time
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        await Assert.That(result).IsNull();
        await Assert.That(_db.Events.Count(e => e.ExternalActivityId == "strava-activity-3")).IsEqualTo(1);
    }

    [Test]
    public async Task Handle_NoSyncConnection_Throws()
    {
        // Arrange — no SeedConnection() call
        var command = new ConfirmStravaActivityCommand(UserId, "strava-activity-4", "Orphan Activity", new DateOnly(2026, 5, 6), 1800);

        // Act / Assert
        await Assert.That(async () => await _handler.Handle(command, CancellationToken.None))
            .Throws<InvalidOperationException>();
    }
}
