using Anthropic.SDK.Messaging;

namespace Pacevite.Api.Infrastructure.Chat;

public interface IAnthropicMessageService
{
    IAsyncEnumerable<MessageResponse> StreamAsync(MessageParameters parameters, CancellationToken ct);
}
