using Pacevite.Api.Features.Sync.ConfirmActivity;
using TUnit.Core;

namespace Pacevite.Api.Tests.Unit.Sync;

[Category("Unit")]
public sealed class ConfirmStravaActivityValidatorTests
{
    private readonly ConfirmStravaActivityValidator _validator = new();

    private static ConfirmStravaActivityCommand ValidCommand() => new(
        UserId: "user-confirm-validator-test",
        ExternalActivityId: "strava-activity-1",
        EventName: "Sunday Long Run",
        EventDate: new DateOnly(2026, 5, 3),
        ElapsedSecs: 5400);

    [Test]
    public async Task Validate_ValidCommand_HasNoErrors()
    {
        var result = await _validator.ValidateAsync(ValidCommand());

        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task Validate_EmptyExternalActivityId_HasError()
    {
        var command = ValidCommand() with { ExternalActivityId = "" };

        var result = await _validator.ValidateAsync(command);

        await Assert.That(result.Errors.Any(e => e.PropertyName == nameof(ConfirmStravaActivityCommand.ExternalActivityId))).IsTrue();
    }

    [Test]
    public async Task Validate_ZeroElapsedSecs_HasError()
    {
        var command = ValidCommand() with { ElapsedSecs = 0 };

        var result = await _validator.ValidateAsync(command);

        await Assert.That(result.Errors.Any(e => e.PropertyName == nameof(ConfirmStravaActivityCommand.ElapsedSecs))).IsTrue();
    }

    [Test]
    public async Task Validate_EmptyEventName_HasError()
    {
        var command = ValidCommand() with { EventName = "" };

        var result = await _validator.ValidateAsync(command);

        await Assert.That(result.Errors.Any(e => e.PropertyName == nameof(ConfirmStravaActivityCommand.EventName))).IsTrue();
    }
}
