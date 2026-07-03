using Mediator;
using Microsoft.EntityFrameworkCore;
using Pacevite.Api.Contracts.Responses;
using Pacevite.Api.Domain.Entities;
using Pacevite.Api.Domain.Enums;
using Pacevite.Api.Infrastructure.Persistence;

namespace Pacevite.Api.Features.Events.CreateEvent;

public sealed class CreateEventHandler(AppDbContext db, ILogger<CreateEventHandler> logger)
    : ICommandHandler<CreateEventCommand, EventResponse?>
{
    public async ValueTask<EventResponse?> Handle(CreateEventCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var eventType = Enum.Parse<EventType>(command.EventType, ignoreCase: true);
            var completion = Enum.Parse<CompletionStatus>(command.Completion, ignoreCase: true);

            var duplicateExists = await db.Events.AnyAsync(e =>
                    e.UserId == command.UserId
                    && e.EventType == eventType
                    && e.EventName == command.EventName
                    && e.EventDate == command.EventDate,
                cancellationToken);

            if (duplicateExists)
            {
                logger.LogInformation(
                    "Duplicate manual event rejected: {EventType} {EventName} {EventDate} for {UserId}",
                    eventType, command.EventName, command.EventDate, command.UserId);
                return null;
            }

            var ev = new Event
            {
                UserId = command.UserId,
                EventType = eventType,
                EventName = command.EventName,
                EventDate = command.EventDate,
                Completion = completion,
                ElapsedSecs = command.ElapsedSecs,
                OverallRank = command.OverallRank,
                AgeGroupRank = command.AgeGroupRank,
                FieldSize = command.FieldSize,
                AgeGroupFieldSize = command.AgeGroupFieldSize
            };

            foreach (var split in command.Splits)
            {
                ev.Splits.Add(new EventSplit
                {
                    EventId = ev.Id,
                    SplitType = split.SplitType,
                    SplitLabel = split.SplitLabel,
                    SplitSecs = split.SplitSecs,
                    CumulativeSecs = split.CumulativeSecs
                });
            }

            db.Events.Add(ev);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Event {EventId} manually created by {UserId}", ev.Id, command.UserId);
            return EventMapper.ToResponse(ev);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "CreateEventHandler failed for {UserId}", command.UserId);
            throw;
        }
    }
}
