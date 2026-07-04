using Mediator;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Pacevite.Api.Contracts.Responses;
using Pacevite.Api.Domain.Enums;
using Pacevite.Api.Infrastructure.Persistence;
using Pacevite.Api.Infrastructure.Sync;

namespace Pacevite.Api.Features.Sync.ListActivities;

public sealed class GetStravaActivitiesHandler(
    AppDbContext db,
    IStravaClient stravaClient,
    IDataProtectionProvider dataProtectionProvider,
    ILogger<GetStravaActivitiesHandler> logger)
    : IQueryHandler<GetStravaActivitiesQuery, IReadOnlyList<StravaActivityPreviewResponse>?>
{
    // Refresh a little before the real expiry so the access token used for the
    // upcoming request doesn't expire mid-flight.
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(2);

    public async ValueTask<IReadOnlyList<StravaActivityPreviewResponse>?> Handle(
        GetStravaActivitiesQuery query, CancellationToken cancellationToken)
    {
        try
        {
            var connection = await db.SyncConnections.FirstOrDefaultAsync(
                sc => sc.UserId == query.UserId && sc.Platform == SyncPlatform.Strava, cancellationToken);

            if (connection is null)
            {
                logger.LogInformation("No Strava connection for {UserId}", query.UserId);
                return null;
            }

            var tokenProtector = dataProtectionProvider.CreateProtector(SyncTokenProtection.Purpose);
            var accessToken = tokenProtector.Unprotect(connection.AccessTokenEncrypted);

            if (connection.ExpiresAt <= DateTimeOffset.UtcNow.Add(RefreshSkew))
            {
                var refreshToken = tokenProtector.Unprotect(connection.RefreshTokenEncrypted);
                var refreshed = await stravaClient.RefreshTokenAsync(refreshToken, cancellationToken);

                connection.AccessTokenEncrypted = tokenProtector.Protect(refreshed.AccessToken);
                connection.RefreshTokenEncrypted = tokenProtector.Protect(refreshed.RefreshToken);
                connection.ExpiresAt = refreshed.ExpiresAt;
                await db.SaveChangesAsync(cancellationToken);

                accessToken = refreshed.AccessToken;
                logger.LogInformation("Refreshed Strava token for {UserId}", query.UserId);
            }

            var activities = await stravaClient.GetRecentActivitiesAsync(accessToken, cancellationToken);

            var userEvents = await db.Events
                .Where(e => e.UserId == query.UserId)
                .Select(e => new UserEventKey(e.EventDate, e.ExternalActivityId))
                .ToListAsync(cancellationToken);

            var alreadyImportedIds = userEvents
                .Where(e => e.ExternalActivityId != null)
                .Select(e => e.ExternalActivityId!)
                .ToHashSet();
            var existingDates = userEvents.Select(e => e.EventDate).ToHashSet();

            return activities
                .Where(a => !alreadyImportedIds.Contains(a.Id.ToString()))
                .Select(a =>
                {
                    var eventDate = DateOnly.FromDateTime(a.StartDate.UtcDateTime);
                    return new StravaActivityPreviewResponse(
                        a.Id.ToString(),
                        a.Name,
                        eventDate,
                        a.ElapsedTimeSecs,
                        existingDates.Contains(eventDate));
                })
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "GetStravaActivitiesHandler failed for {UserId}", query.UserId);
            throw;
        }
    }

    private readonly record struct UserEventKey(DateOnly EventDate, string? ExternalActivityId);
}
