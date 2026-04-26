namespace Pacevite.Api.Features.Events.PredictionCoaching;

public sealed class PredictionCoachingHandler
{
    public Task HandleAsync(HttpContext context, string userId, string eventType, CancellationToken ct)
    {
        context.Response.StatusCode = StatusCodes.Status501NotImplemented;
        return Task.CompletedTask;
    }
}
