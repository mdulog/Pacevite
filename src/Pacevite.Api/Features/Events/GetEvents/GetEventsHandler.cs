using Mediator;
using Microsoft.EntityFrameworkCore;
using Pacevite.Api.Contracts.Responses;
using Pacevite.Api.Domain.Enums;
using Pacevite.Api.Infrastructure.Persistence;

namespace Pacevite.Api.Features.Events.GetEvents;

public sealed class GetEventsHandler(AppDbContext db, ILogger<GetEventsHandler> logger)
    : IQueryHandler<GetEventsQuery, PagedEventsResponse>
{
    public async ValueTask<PagedEventsResponse> Handle(
        GetEventsQuery query, CancellationToken cancellationToken)
    {
        try
        {
            var q = db.Events.Where(e => e.UserId == query.UserId);

            if (query.EventType is not null && Enum.TryParse<EventType>(query.EventType, ignoreCase: true, out var eventType))
                q = q.Where(e => e.EventType == eventType);

            if (query.From.HasValue)
                q = q.Where(e => e.EventDate >= query.From.Value);

            if (query.To.HasValue)
                q = q.Where(e => e.EventDate <= query.To.Value);

            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var pattern = $"%{EscapeLikePattern(query.Search)}%";
                q = q.Where(e => EF.Functions.ILike(e.EventName, pattern, @"\"));
            }

            if (EventCursor.TryDecode(query.Cursor, out var cursor))
            {
                q = q.Where(e => e.EventDate < cursor.EventDate
                    || (e.EventDate == cursor.EventDate && e.Id.CompareTo(cursor.Id) < 0));
            }

            // Fetch one extra row to know whether a next page exists without a COUNT query.
            var events = await q
                .OrderByDescending(e => e.EventDate)
                .ThenByDescending(e => e.Id)
                .Take(query.Limit + 1)
                .ToListAsync(cancellationToken);

            var hasMore = events.Count > query.Limit;
            var page = hasMore ? events[..query.Limit] : events;
            var last = page.Count > 0 ? page[^1] : null;
            var nextCursor = hasMore && last is not null
                ? new EventCursor(last.EventDate, last.Id).Encode()
                : null;

            return new PagedEventsResponse(
                page.Select(EventMapper.ToSummaryResponse).ToList(),
                nextCursor);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "GetEventsHandler failed for {UserId}", query.UserId);
            throw;
        }
    }

    // ILIKE treats % and _ as wildcards; escape them (and the escape char itself)
    // so user input always matches literally.
    private static string EscapeLikePattern(string input) =>
        input.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
}
