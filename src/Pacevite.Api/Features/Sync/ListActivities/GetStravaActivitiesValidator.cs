using FluentValidation;

namespace Pacevite.Api.Features.Sync.ListActivities;

public sealed class GetStravaActivitiesValidator : AbstractValidator<GetStravaActivitiesQuery>
{
    public GetStravaActivitiesValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
    }
}
