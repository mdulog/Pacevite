using System.Security.Claims;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Pacevite.Api.Contracts.Requests;
using Pacevite.Api.Contracts.Responses;
using Pacevite.Api.Features.Sync.ConfirmActivity;
using Pacevite.Api.Features.Sync.ConnectStrava;
using Pacevite.Api.Features.Sync.ListActivities;
using Pacevite.Api.Features.Sync.StravaCallback;

namespace Pacevite.Api.Features.Sync;

public static class SyncEndpoints
{
    public static IEndpointRouteBuilder MapSyncEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/strava/connect", ConnectStravaAsync)
            .WithName("ConnectStrava")
            .RequireAuthorization();

        // Anonymous: this is the browser's redirect target from Strava, so no Pacevite
        // session is available yet — the signed `state` parameter carries the user's
        // identity instead (see StravaState).
        app.MapGet("/strava/callback", StravaCallbackAsync)
            .WithName("StravaCallback")
            .AllowAnonymous();

        app.MapGet("/strava/activities", GetStravaActivitiesAsync)
            .WithName("GetStravaActivities")
            .RequireAuthorization();

        app.MapPost("/strava/activities/confirm", ConfirmStravaActivityAsync)
            .WithName("ConfirmStravaActivity")
            .RequireAuthorization();

        return app;
    }

    private static async Task<Ok<ConnectStravaResponse>> ConnectStravaAsync(
        ClaimsPrincipal user,
        IMediator mediator,
        CancellationToken ct)
    {
        var userId = GetUserId(user);
        var authorizeUrl = await mediator.Send(new ConnectStravaQuery(userId), ct);
        return TypedResults.Ok(new ConnectStravaResponse(authorizeUrl));
    }

    private static async Task<RedirectHttpResult> StravaCallbackAsync(
        string code,
        string state,
        IMediator mediator,
        CancellationToken ct)
    {
        var result = await mediator.Send(new HandleStravaCallbackCommand(code, state), ct);
        return TypedResults.Redirect(result is null ? "/sync?connected=false" : "/sync?connected=true");
    }

    private static async Task<Results<Ok<IReadOnlyList<StravaActivityPreviewResponse>>, Conflict<string>>> GetStravaActivitiesAsync(
        ClaimsPrincipal user,
        IMediator mediator,
        CancellationToken ct)
    {
        var userId = GetUserId(user);
        var result = await mediator.Send(new GetStravaActivitiesQuery(userId), ct);

        return result is null
            ? TypedResults.Conflict("No Strava connection found — connect Strava first.")
            : TypedResults.Ok(result);
    }

    private static async Task<Results<Created<EventResponse>, Conflict<string>>> ConfirmStravaActivityAsync(
        ConfirmStravaActivityRequest request,
        ClaimsPrincipal user,
        IMediator mediator,
        CancellationToken ct)
    {
        var userId = GetUserId(user);
        var result = await mediator.Send(
            new ConfirmStravaActivityCommand(userId, request.ExternalActivityId, request.Name, request.EventDate, request.ElapsedSecs),
            ct);

        return result is null
            ? TypedResults.Conflict($"Strava activity '{request.ExternalActivityId}' was already imported.")
            : TypedResults.Created($"/api/events/{result.Id}", result);
    }

    private static string GetUserId(ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? user.FindFirstValue("sub")
        ?? throw new InvalidOperationException("User ID claim missing from token.");
}
