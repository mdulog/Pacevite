using FluentValidation;

namespace Pacevite.Api.Features.Sync.ConfirmActivity;

public sealed class ConfirmStravaActivityValidator : AbstractValidator<ConfirmStravaActivityCommand>
{
    public ConfirmStravaActivityValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.ExternalActivityId).NotEmpty();
        RuleFor(x => x.EventName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.EventDate).NotEmpty();
        RuleFor(x => x.ElapsedSecs).GreaterThan(0);
    }
}
