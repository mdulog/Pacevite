using Mediator;
using Pacevite.Api.Infrastructure.Auth;

namespace Pacevite.Api.Features.Auth.Logout;

public sealed class LogoutHandler(
    IRefreshTokenService refreshTokenService,
    ILogger<LogoutHandler> logger) : ICommandHandler<LogoutCommand, bool>
{
    public async ValueTask<bool> Handle(LogoutCommand command, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(command.RawToken))
        {
            await refreshTokenService.RevokeAsync(command.RawToken, cancellationToken);
            logger.LogInformation("Refresh token revoked on logout");
        }

        return true;
    }
}
