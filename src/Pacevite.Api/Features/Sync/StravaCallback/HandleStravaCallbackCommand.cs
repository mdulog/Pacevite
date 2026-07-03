using Mediator;
using Pacevite.Api.Contracts.Responses;

namespace Pacevite.Api.Features.Sync.StravaCallback;

// UserId is null until the signed `state` parameter is validated inside the handler —
// the callback is an anonymous redirect target, so the caller's identity comes from
// state, not from the ambient ClaimsPrincipal.
public sealed record HandleStravaCallbackCommand(string Code, string State) : ICommand<SyncConnectionResponse?>;
