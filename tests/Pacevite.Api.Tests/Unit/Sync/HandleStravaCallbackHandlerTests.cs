using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Pacevite.Api.Domain.Enums;
using Pacevite.Api.Features.Sync.StravaCallback;
using Pacevite.Api.Infrastructure.Persistence;
using Pacevite.Api.Infrastructure.Sync;
using TUnit.Core;

namespace Pacevite.Api.Tests.Unit.Sync;

[Category("Unit")]
public sealed class HandleStravaCallbackHandlerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly FakeStravaClient _stravaClient = new();
    private readonly IDataProtectionProvider _dataProtection = new EphemeralDataProtectionProvider();
    private readonly HandleStravaCallbackHandler _handler;
    private const string UserId = "user-callback-strava-test";

    public HandleStravaCallbackHandlerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _handler = new HandleStravaCallbackHandler(
            _db, _stravaClient, _dataProtection, NullLogger<HandleStravaCallbackHandler>.Instance);
    }

    public void Dispose() => _db.Dispose();

    [Test]
    public async Task Handle_ValidState_ExchangesCodeAndCreatesConnection()
    {
        // Arrange
        var state = StravaState.Create(_dataProtection, UserId);
        var command = new HandleStravaCallbackCommand("auth-code-123", state);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Platform).IsEqualTo("STRAVA");
        await Assert.That(_stravaClient.LastExchangedCode).IsEqualTo("auth-code-123");

        var saved = await _db.SyncConnections.SingleAsync(sc => sc.UserId == UserId);
        await Assert.That(saved.Platform).IsEqualTo(SyncPlatform.Strava);
    }

    [Test]
    public async Task Handle_ValidState_StoresTokensEncryptedNotRaw()
    {
        // Arrange
        var state = StravaState.Create(_dataProtection, UserId);
        var command = new HandleStravaCallbackCommand("auth-code-123", state);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        var saved = await _db.SyncConnections.SingleAsync(sc => sc.UserId == UserId);
        await Assert.That(saved.AccessTokenEncrypted).IsNotEqualTo(_stravaClient.TokenToReturn.AccessToken);

        var protector = _dataProtection.CreateProtector(SyncTokenProtection.Purpose);
        await Assert.That(protector.Unprotect(saved.AccessTokenEncrypted)).IsEqualTo(_stravaClient.TokenToReturn.AccessToken);
    }

    [Test]
    public async Task Handle_InvalidState_ReturnsNullWithoutCallingStrava()
    {
        // Arrange
        var command = new HandleStravaCallbackCommand("auth-code-123", "forged-state-value");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        await Assert.That(result).IsNull();
        await Assert.That(_stravaClient.LastExchangedCode).IsNull();
    }

    [Test]
    public async Task Handle_ExistingConnection_UpdatesInPlaceRatherThanDuplicating()
    {
        // Arrange — connect once
        var firstState = StravaState.Create(_dataProtection, UserId);
        await _handler.Handle(new HandleStravaCallbackCommand("code-1", firstState), CancellationToken.None);

        _stravaClient.TokenToReturn = new StravaTokenResult
        {
            AccessToken = "second-access-token",
            RefreshToken = "second-refresh-token",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(6),
            AthleteId = "athlete-1"
        };

        // Act — reconnect
        var secondState = StravaState.Create(_dataProtection, UserId);
        await _handler.Handle(new HandleStravaCallbackCommand("code-2", secondState), CancellationToken.None);

        // Assert
        await Assert.That(_db.SyncConnections.Count(sc => sc.UserId == UserId)).IsEqualTo(1);
        var saved = await _db.SyncConnections.SingleAsync(sc => sc.UserId == UserId);
        var protector = _dataProtection.CreateProtector(SyncTokenProtection.Purpose);
        await Assert.That(protector.Unprotect(saved.AccessTokenEncrypted)).IsEqualTo("second-access-token");
    }
}
