using Pacevite.Api.Features.Sync.StravaCallback;
using TUnit.Core;

namespace Pacevite.Api.Tests.Unit.Sync;

[Category("Unit")]
public sealed class HandleStravaCallbackValidatorTests
{
    private readonly HandleStravaCallbackValidator _validator = new();

    [Test]
    public async Task Validate_ValidCommand_HasNoErrors()
    {
        var result = await _validator.ValidateAsync(new HandleStravaCallbackCommand("auth-code", "signed-state"));

        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task Validate_EmptyCode_HasError()
    {
        var result = await _validator.ValidateAsync(new HandleStravaCallbackCommand("", "signed-state"));

        await Assert.That(result.Errors.Any(e => e.PropertyName == nameof(HandleStravaCallbackCommand.Code))).IsTrue();
    }

    [Test]
    public async Task Validate_EmptyState_HasError()
    {
        var result = await _validator.ValidateAsync(new HandleStravaCallbackCommand("auth-code", ""));

        await Assert.That(result.Errors.Any(e => e.PropertyName == nameof(HandleStravaCallbackCommand.State))).IsTrue();
    }
}
