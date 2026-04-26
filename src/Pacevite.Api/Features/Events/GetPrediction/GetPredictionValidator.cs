using FluentValidation;
using Pacevite.Api.Domain.Enums;

namespace Pacevite.Api.Features.Events.GetPrediction;

public sealed class GetPredictionValidator : AbstractValidator<GetPredictionQuery>
{
    private static readonly HashSet<string> ValidEventTypes =
        Enum.GetNames<EventType>().ToHashSet(StringComparer.OrdinalIgnoreCase);

    public GetPredictionValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.EventType)
            .NotEmpty()
            .Must(t => ValidEventTypes.Contains(t))
            .WithMessage(x =>
                $"'{x.EventType}' is not a valid event type. Valid: {string.Join(", ", Enum.GetNames<EventType>())}");
    }
}
