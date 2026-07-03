using FluentValidation;
using Pacevite.Api.Domain.Enums;

namespace Pacevite.Api.Features.Events.CreateEvent;

public sealed class CreateEventValidator : AbstractValidator<CreateEventCommand>
{
    public CreateEventValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();

        RuleFor(x => x.EventType)
            .NotEmpty()
            .Must(t => Enum.TryParse<EventType>(t, ignoreCase: true, out _))
            .WithMessage("EventType must be one of: Marathon, Hyrox, Spartan, Generic.");

        RuleFor(x => x.EventName).NotEmpty().MaximumLength(200);

        RuleFor(x => x.EventDate).NotEmpty();

        RuleFor(x => x.Completion)
            .NotEmpty()
            .Must(c => Enum.TryParse<CompletionStatus>(c, ignoreCase: true, out _))
            .WithMessage("Completion must be one of: Finished, Dnf, Dns.");

        RuleFor(x => x.ElapsedSecs).GreaterThan(0);

        RuleFor(x => x.OverallRank).GreaterThan(0).When(x => x.OverallRank.HasValue);
        RuleFor(x => x.AgeGroupRank).GreaterThan(0).When(x => x.AgeGroupRank.HasValue);
        RuleFor(x => x.FieldSize).GreaterThan(0).When(x => x.FieldSize.HasValue);
        RuleFor(x => x.AgeGroupFieldSize).GreaterThan(0).When(x => x.AgeGroupFieldSize.HasValue);

        RuleForEach(x => x.Splits).ChildRules(split =>
        {
            split.RuleFor(s => s.SplitType).NotEmpty();
            split.RuleFor(s => s.SplitLabel).NotEmpty();
            split.RuleFor(s => s.SplitSecs).GreaterThan(0);
            split.RuleFor(s => s.CumulativeSecs).GreaterThan(0);
        });
    }
}
