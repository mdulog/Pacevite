using FluentValidation;

namespace Pacevite.Api.Features.Sync.ConnectStrava;

public sealed class ConnectStravaValidator : AbstractValidator<ConnectStravaQuery>
{
    public ConnectStravaValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
    }
}
