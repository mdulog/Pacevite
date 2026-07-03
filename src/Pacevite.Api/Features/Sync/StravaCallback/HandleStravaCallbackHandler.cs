using Mediator;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Pacevite.Api.Contracts.Responses;
using Pacevite.Api.Domain.Entities;
using Pacevite.Api.Domain.Enums;
using Pacevite.Api.Infrastructure.Persistence;
using Pacevite.Api.Infrastructure.Sync;

namespace Pacevite.Api.Features.Sync.StravaCallback;

public sealed class HandleStravaCallbackHandler(
    AppDbContext db,
    IStravaClient stravaClient,
    IDataProtectionProvider dataProtectionProvider,
    ILogger<HandleStravaCallbackHandler> logger) : ICommandHandler<HandleStravaCallbackCommand, SyncConnectionResponse?>
{
    public async ValueTask<SyncConnectionResponse?> Handle(HandleStravaCallbackCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var userId = StravaState.TryUnprotect(dataProtectionProvider, command.State);
            if (userId is null)
            {
                logger.LogWarning("Strava callback rejected — state was missing, forged, or expired.");
                return null;
            }

            var tokens = await stravaClient.ExchangeCodeAsync(command.Code, cancellationToken);
            var tokenProtector = dataProtectionProvider.CreateProtector(SyncTokenProtection.Purpose);

            var connection = await db.SyncConnections.FirstOrDefaultAsync(
                sc => sc.UserId == userId && sc.Platform == SyncPlatform.Strava, cancellationToken);

            if (connection is null)
            {
                connection = new SyncConnection
                {
                    UserId = userId,
                    Platform = SyncPlatform.Strava,
                    ExternalAthleteId = tokens.AthleteId,
                    AccessTokenEncrypted = tokenProtector.Protect(tokens.AccessToken),
                    RefreshTokenEncrypted = tokenProtector.Protect(tokens.RefreshToken),
                    ExpiresAt = tokens.ExpiresAt
                };
                db.SyncConnections.Add(connection);
            }
            else
            {
                connection.AccessTokenEncrypted = tokenProtector.Protect(tokens.AccessToken);
                connection.RefreshTokenEncrypted = tokenProtector.Protect(tokens.RefreshToken);
                connection.ExpiresAt = tokens.ExpiresAt;
            }

            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Strava connection saved for {UserId}", userId);
            return new SyncConnectionResponse(connection.Platform.ToString().ToUpperInvariant(), connection.ConnectedAt);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "HandleStravaCallbackHandler failed");
            throw;
        }
    }
}
