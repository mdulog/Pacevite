using Mediator;
using Microsoft.AspNetCore.Identity;
using Pacevite.Api.Infrastructure.Auth;

namespace Pacevite.Api.Features.Auth.Login;

public sealed class LoginHandler(
    UserManager<IdentityUser> userManager,
    IJwtTokenService jwtTokenService,
    IRefreshTokenService refreshTokenService,
    ILogger<LoginHandler> logger) : ICommandHandler<LoginCommand, AuthResult>
{
    public async ValueTask<AuthResult> Handle(LoginCommand command, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(command.Email);

        // Do not reveal whether the email exists — OWASP A07
        if (user is null || !await userManager.CheckPasswordAsync(user, command.Password))
        {
            if (user is not null)
                logger.LogWarning("Failed login attempt for {UserId}", user.Id);

            return AuthResult.Fail("Invalid credentials.");
        }

        logger.LogInformation("User logged in: {UserId}", user.Id);
        var token = jwtTokenService.GenerateToken(user);
        var rawRefreshToken = await refreshTokenService.CreateAsync(user.Id, cancellationToken);
        return AuthResult.Ok(user.Id, user.Email!, token, rawRefreshToken);
    }
}
