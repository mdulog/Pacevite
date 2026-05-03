using FluentValidation;
using Pacevite.Api.Domain.Enums;

namespace Pacevite.Api.Features.Events.GetEvents;

public sealed class GetEventsValidator : AbstractValidator<GetEventsQuery>
{
    private static readonly string[] ValidEventTypes =
        Enum.GetNames<EventType>();

    public GetEventsValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();

        RuleFor(x => x.EventType)
            .Must(v => ValidEventTypes.Contains(v, StringComparer.OrdinalIgnoreCase))
            .When(x => x.EventType is not null)
            .WithMessage($"EventType must be one of: {string.Join(", ", ValidEventTypes)}.");

        RuleFor(x => x)
            .Must(x => x.From <= x.To)
            .When(x => x.From.HasValue && x.To.HasValue)
            .WithMessage("From must not be later than To.");
    }
}
