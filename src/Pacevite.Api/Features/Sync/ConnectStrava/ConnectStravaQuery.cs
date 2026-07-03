using Mediator;

namespace Pacevite.Api.Features.Sync.ConnectStrava;

public sealed record ConnectStravaQuery(string UserId) : IQuery<string>;
