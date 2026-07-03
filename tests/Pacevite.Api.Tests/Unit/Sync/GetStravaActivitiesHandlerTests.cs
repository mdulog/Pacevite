using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Pacevite.Api.Domain.Entities;
using Pacevite.Api.Domain.Enums;
using Pacevite.Api.Features.Sync.ListActivities;
using Pacevite.Api.Infrastructure.Persistence;
using Pacevite.Api.Infrastructure.Sync;
using TUnit.Core;

namespace Pacevite.Api.Tests.Unit.Sync;

[Category("Unit")]
public sealed class GetStravaActivitiesHandlerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly FakeStravaClient _stravaClient = new();
    private readonly IDataProtectionProvider _dataProtection = new EphemeralDataProtectionProvider();
    private readonly GetStravaActivitiesHandler _handler;
    private const string UserId = "user-list-strava-test";

    public GetStravaActivitiesHandlerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _handler = new GetStravaActivitiesHandler(
            _db, _stravaClient, _dataProtection, NullLogger<GetStravaActivitiesHandler>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private SyncConnection SeedConnection(DateTimeOffset expiresAt)
    {
        var protector = _dataProtection.CreateProtector(SyncTokenProtection.Purpose);
        var connection = new SyncConnection
        {
            UserId = UserId,
            Platform = SyncPlatform.Strava,
            ExternalAthleteId = "athlete-1",
            AccessTokenEncrypted = protector.Protect("valid-access-token"),
            RefreshTokenEncrypted = protector.Protect("valid-refresh-token"),
            ExpiresAt = expiresAt
        };
        _db.SyncConnections.Add(connection);
        _db.SaveChanges();
        return connection;
    }

    [Test]
    public async Task Handle_NoConnection_ReturnsNull()
    {
        // Arrange
        var query = new GetStravaActivitiesQuery(UserId);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Handle_ValidToken_UsesStoredAccessTokenWithoutRefreshing()
    {
        // Arrange
        SeedConnection(DateTimeOffset.UtcNow.AddHours(6));
        _stravaClient.ActivitiesToReturn = [];
        var query = new GetStravaActivitiesQuery(UserId);

        // Act
        await _handler.Handle(query, CancellationToken.None);

        // Assert
        await Assert.That(_stravaClient.LastAccessTokenUsedForActivities).IsEqualTo("valid-access-token");
        await Assert.That(_stravaClient.RefreshCallCount).IsEqualTo(0);
    }

    [Test]
    public async Task Handle_ExpiredToken_RefreshesBeforeFetchingActivities()
    {
        // Arrange
        SeedConnection(DateTimeOffset.UtcNow.AddMinutes(-5));
        _stravaClient.TokenToReturn = new StravaTokenResult
        {
            AccessToken = "refreshed-access-token",
            RefreshToken = "refreshed-refresh-token",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(6),
            AthleteId = "athlete-1"
        };
        var query = new GetStravaActivitiesQuery(UserId);

        // Act
        await _handler.Handle(query, CancellationToken.None);

        // Assert
        await Assert.That(_stravaClient.RefreshCallCount).IsEqualTo(1);
        await Assert.That(_stravaClient.LastRefreshedToken).IsEqualTo("valid-refresh-token");
        await Assert.That(_stravaClient.LastAccessTokenUsedForActivities).IsEqualTo("refreshed-access-token");
    }

    [Test]
    public async Task Handle_ExpiredToken_PersistsRefreshedTokensEncrypted()
    {
        // Arrange
        SeedConnection(DateTimeOffset.UtcNow.AddMinutes(-5));
        _stravaClient.TokenToReturn = new StravaTokenResult
        {
            AccessToken = "refreshed-access-token",
            RefreshToken = "refreshed-refresh-token",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(6),
            AthleteId = "athlete-1"
        };
        var query = new GetStravaActivitiesQuery(UserId);

        // Act
        await _handler.Handle(query, CancellationToken.None);

        // Assert
        var saved = await _db.SyncConnections.SingleAsync(sc => sc.UserId == UserId);
        var protector = _dataProtection.CreateProtector(SyncTokenProtection.Purpose);
        await Assert.That(protector.Unprotect(saved.AccessTokenEncrypted)).IsEqualTo("refreshed-access-token");
        await Assert.That(saved.AccessTokenEncrypted).IsNotEqualTo("refreshed-access-token"); // stored encrypted, not raw
    }

    [Test]
    public async Task Handle_ActivityOnSameDateAsExistingEvent_IsFlaggedAsPossibleDuplicate()
    {
        // Arrange
        SeedConnection(DateTimeOffset.UtcNow.AddHours(6));
        _db.Events.Add(new Event
        {
            UserId = UserId,
            EventType = EventType.Marathon,
            EventName = "Manually entered race",
            EventDate = new DateOnly(2026, 5, 3),
            Completion = CompletionStatus.Finished,
            ElapsedSecs = 12000
        });
        _db.SaveChanges();

        _stravaClient.ActivitiesToReturn =
        [
            new StravaActivity { Id = 111, Name = "Sunday Race", Type = "Run", StartDate = new DateTimeOffset(2026, 5, 3, 8, 0, 0, TimeSpan.Zero), ElapsedTimeSecs = 5000 }
        ];
        var query = new GetStravaActivitiesQuery(UserId);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        await Assert.That(result!.Single().PossibleDuplicate).IsTrue();
    }

    [Test]
    public async Task Handle_ActivityOnNewDate_IsNotFlaggedAsPossibleDuplicate()
    {
        // Arrange
        SeedConnection(DateTimeOffset.UtcNow.AddHours(6));
        _stravaClient.ActivitiesToReturn =
        [
            new StravaActivity { Id = 222, Name = "Sunday Race", Type = "Run", StartDate = new DateTimeOffset(2026, 5, 3, 8, 0, 0, TimeSpan.Zero), ElapsedTimeSecs = 5000 }
        ];
        var query = new GetStravaActivitiesQuery(UserId);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        await Assert.That(result!.Single().PossibleDuplicate).IsFalse();
    }

    [Test]
    public async Task Handle_ActivityAlreadyImported_IsExcludedFromResults()
    {
        // Arrange
        var connection = SeedConnection(DateTimeOffset.UtcNow.AddHours(6));
        _db.Events.Add(new Event
        {
            UserId = UserId,
            EventType = EventType.Generic,
            EventName = "Already Imported",
            EventDate = new DateOnly(2026, 5, 3),
            Completion = CompletionStatus.Finished,
            ElapsedSecs = 5000,
            Source = "STRAVA",
            NeedsEnrichment = true,
            ExternalActivityId = "333",
            SyncConnectionId = connection.Id
        });
        _db.SaveChanges();

        _stravaClient.ActivitiesToReturn =
        [
            new StravaActivity { Id = 333, Name = "Already Imported", Type = "Run", StartDate = new DateTimeOffset(2026, 5, 3, 8, 0, 0, TimeSpan.Zero), ElapsedTimeSecs = 5000 },
            new StravaActivity { Id = 444, Name = "New Activity", Type = "Run", StartDate = new DateTimeOffset(2026, 5, 6, 8, 0, 0, TimeSpan.Zero), ElapsedTimeSecs = 3000 }
        ];
        var query = new GetStravaActivitiesQuery(UserId);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        await Assert.That(result!.Count).IsEqualTo(1);
        await Assert.That(result[0].ExternalActivityId).IsEqualTo("444");
    }
}
