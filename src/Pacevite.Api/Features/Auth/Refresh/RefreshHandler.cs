using Mediator;
using Microsoft.AspNetCore.Identity;
using Pacevite.Api.Infrastructure.Auth;

namespace Pacevite.Api.Features.Auth.Refresh;

public sealed class RefreshHandler(
    UserManager<IdentityUser> userManager,
    IJwtTokenService jwtTokenService,
    IRefreshTokenService refreshTokenService,
    ILogger<RefreshHandler> logger) : ICommandHandler<RefreshCommand, RefreshResult>
{
    public async ValueTask<RefreshResult> Handle(RefreshCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.RawToken))
            return RefreshResult.Fail();

        var (valid, userId, newRawToken) = await refreshTokenService.RotateAsync(command.RawToken, cancellationToken);

        if (!valid || userId is null || newRawToken is null)
            return RefreshResult.Fail();

        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            logger.LogWarning("Refresh token valid but user {UserId} not found", userId);
            return RefreshResult.Fail();
        }

        return RefreshResult.Ok(jwtTokenService.GenerateToken(user), newRawToken);
    }
}
