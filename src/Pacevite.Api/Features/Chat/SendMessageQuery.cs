using Mediator;
using Pacevite.Api.Infrastructure.Chat;

namespace Pacevite.Api.Features.Chat;

public sealed record SendMessageQuery(
    string UserId,
    string Message,
    IReadOnlyList<ConversationMessage> History) : IStreamQuery<SseEvent>;
