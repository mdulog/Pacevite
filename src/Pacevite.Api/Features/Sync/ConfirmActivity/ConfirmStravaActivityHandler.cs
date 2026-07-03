using Mediator;
using Microsoft.EntityFrameworkCore;
using Pacevite.Api.Contracts.Responses;
using Pacevite.Api.Domain.Entities;
using Pacevite.Api.Domain.Enums;
using Pacevite.Api.Features.Events;
using Pacevite.Api.Infrastructure.Persistence;

namespace Pacevite.Api.Features.Sync.ConfirmActivity;

public sealed class ConfirmStravaActivityHandler(AppDbContext db, ILogger<ConfirmStravaActivityHandler> logger)
    : ICommandHandler<ConfirmStravaActivityCommand, EventResponse?>
{
    public async ValueTask<EventResponse?> Handle(ConfirmStravaActivityCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var alreadyImported = await db.Events.AnyAsync(
                e => e.UserId == command.UserId && e.ExternalActivityId == command.ExternalActivityId,
                cancellationToken);

            if (alreadyImported)
            {
                logger.LogInformation(
                    "Strava activity {ExternalActivityId} already imported for {UserId}",
                    command.ExternalActivityId, command.UserId);
                return null;
            }

            var connection = await db.SyncConnections.FirstOrDefaultAsync(
                    sc => sc.UserId == command.UserId && sc.Platform == SyncPlatform.Strava, cancellationToken)
                ?? throw new InvalidOperationException($"No Strava connection found for {command.UserId}.");

            // A synced activity can't supply placement or official splits — same rule as GPX.
            var ev = new Event
            {
                UserId = command.UserId,
                EventType = EventType.Generic,
                EventName = command.EventName,
                EventDate = command.EventDate,
                Completion = CompletionStatus.Finished,
                ElapsedSecs = command.ElapsedSecs,
                Source = "STRAVA",
                NeedsEnrichment = true,
                ExternalActivityId = command.ExternalActivityId,
                SyncConnectionId = connection.Id
            };

            db.Events.Add(ev);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Strava activity {ExternalActivityId} confirmed as event {EventId} for {UserId}",
                command.ExternalActivityId, ev.Id, command.UserId);

            return EventMapper.ToResponse(ev);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "ConfirmStravaActivityHandler failed for {UserId}", command.UserId);
            throw;
        }
    }
}
