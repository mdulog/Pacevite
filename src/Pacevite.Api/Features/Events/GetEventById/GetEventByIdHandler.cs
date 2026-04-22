using Mediator;
using Microsoft.EntityFrameworkCore;
using Pacevite.Api.Contracts.Responses;
using Pacevite.Api.Infrastructure.Persistence;

namespace Pacevite.Api.Features.Events.GetEventById;

public sealed class GetEventByIdHandler(AppDbContext db)
    : IQueryHandler<GetEventByIdQuery, EventResponse?>
{
    public async ValueTask<EventResponse?> Handle(
        GetEventByIdQuery query, CancellationToken cancellationToken)
    {
        var ev = await db.Events
            .Include(e => e.Splits.OrderBy(s => s.CumulativeSecs))
            .Where(e => e.Id == query.EventId && e.UserId == query.UserId)
            .FirstOrDefaultAsync(cancellationToken);

        return ev is null ? null : EventMapper.ToResponse(ev);
    }
}
