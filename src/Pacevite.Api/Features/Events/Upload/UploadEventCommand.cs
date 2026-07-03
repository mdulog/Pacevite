using Mediator;
using Pacevite.Api.Contracts.Responses;

namespace Pacevite.Api.Features.Events.Upload;

public sealed record UploadEventCommand(
    string UserId,
    string ContentType,
    string FileName,
    Stream FileStream) : ICommand<IReadOnlyList<EventResponse>>;
