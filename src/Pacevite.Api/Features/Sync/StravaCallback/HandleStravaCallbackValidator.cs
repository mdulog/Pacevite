using FluentValidation;

namespace Pacevite.Api.Features.Sync.StravaCallback;

public sealed class HandleStravaCallbackValidator : AbstractValidator<HandleStravaCallbackCommand>
{
    public HandleStravaCallbackValidator()
    {
        RuleFor(x => x.Code).NotEmpty();
        RuleFor(x => x.State).NotEmpty();
    }
}
