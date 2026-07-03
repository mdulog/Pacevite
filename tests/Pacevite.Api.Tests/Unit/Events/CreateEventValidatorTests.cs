using Pacevite.Api.Features.Events.CreateEvent;
using TUnit.Core;

namespace Pacevite.Api.Tests.Unit.Events;

[Category("Unit")]
public sealed class CreateEventValidatorTests
{
    private readonly CreateEventValidator _validator = new();

    private static CreateEventCommand ValidCommand() => new(
        UserId: "user-create-event-test",
        EventType: "GENERIC",
        EventName: "Local 10K",
        EventDate: new DateOnly(2026, 5, 1),
        Completion: "FINISHED",
        ElapsedSecs: 2700,
        OverallRank: 42,
        AgeGroupRank: 5,
        FieldSize: 500,
        AgeGroupFieldSize: 60,
        Splits: []);

    [Test]
    public async Task Validate_ValidCommand_HasNoErrors()
    {
        // Arrange
        var command = ValidCommand();

        // Act
        var result = await _validator.ValidateAsync(command);

        // Assert
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task Validate_UnknownEventType_HasError()
    {
        // Arrange
        var command = ValidCommand() with { EventType = "TRIATHLON" };

        // Act
        var result = await _validator.ValidateAsync(command);

        // Assert
        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Any(e => e.PropertyName == nameof(CreateEventCommand.EventType))).IsTrue();
    }

    [Test]
    public async Task Validate_UnknownCompletion_HasError()
    {
        // Arrange
        var command = ValidCommand() with { Completion = "WITHDREW" };

        // Act
        var result = await _validator.ValidateAsync(command);

        // Assert
        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Any(e => e.PropertyName == nameof(CreateEventCommand.Completion))).IsTrue();
    }

    [Test]
    public async Task Validate_ZeroElapsedSecs_HasError()
    {
        // Arrange
        var command = ValidCommand() with { ElapsedSecs = 0 };

        // Act
        var result = await _validator.ValidateAsync(command);

        // Assert
        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Any(e => e.PropertyName == nameof(CreateEventCommand.ElapsedSecs))).IsTrue();
    }

    [Test]
    public async Task Validate_EmptyEventName_HasError()
    {
        // Arrange
        var command = ValidCommand() with { EventName = "" };

        // Act
        var result = await _validator.ValidateAsync(command);

        // Assert
        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Any(e => e.PropertyName == nameof(CreateEventCommand.EventName))).IsTrue();
    }

    [Test]
    public async Task Validate_SplitWithZeroCumulativeSecs_HasError()
    {
        // Arrange
        var command = ValidCommand() with
        {
            Splits = [new CreateEventSplitInput("RUN", "5km", 1200, 0)]
        };

        // Act
        var result = await _validator.ValidateAsync(command);

        // Assert
        await Assert.That(result.IsValid).IsFalse();
    }
}
