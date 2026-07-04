using Pacevite.Api.Features.Events.GetEvents;
using TUnit.Core;

namespace Pacevite.Api.Tests.Unit.Events;

[Category("Unit")]
public sealed class GetEventsValidatorTests
{
    private readonly GetEventsValidator _validator = new();

    [Test]
    public async Task passes_for_default_query_with_only_user_id()
    {
        // Arrange
        var query = new GetEventsQuery("user-42");

        // Act
        var result = await _validator.ValidateAsync(query);

        // Assert
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    [Arguments(101)]
    public async Task fails_when_limit_is_out_of_bounds(int limit)
    {
        // Arrange
        var query = new GetEventsQuery("user-42", Limit: limit);

        // Act
        var result = await _validator.ValidateAsync(query);

        // Assert
        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    [Arguments(1)]
    [Arguments(20)]
    [Arguments(100)]
    public async Task passes_when_limit_is_within_bounds(int limit)
    {
        // Arrange
        var query = new GetEventsQuery("user-42", Limit: limit);

        // Act
        var result = await _validator.ValidateAsync(query);

        // Assert
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task fails_when_cursor_is_malformed()
    {
        // Arrange
        var query = new GetEventsQuery("user-42", Cursor: "not-a-real-cursor!!!");

        // Act
        var result = await _validator.ValidateAsync(query);

        // Assert
        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async Task passes_when_cursor_is_well_formed()
    {
        // Arrange
        var cursor = new EventCursor(new DateOnly(2024, 9, 29), Guid.NewGuid()).Encode();
        var query = new GetEventsQuery("user-42", Cursor: cursor);

        // Act
        var result = await _validator.ValidateAsync(query);

        // Assert
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task fails_when_search_exceeds_max_length()
    {
        // Arrange
        var query = new GetEventsQuery("user-42", Search: new string('a', 101));

        // Act
        var result = await _validator.ValidateAsync(query);

        // Assert
        await Assert.That(result.IsValid).IsFalse();
    }
}
