using Pacevite.Api.Features.Events.GetEvents;
using TUnit.Core;

namespace Pacevite.Api.Tests.Unit.Events;

[Category("Unit")]
public sealed class EventCursorTests
{
    [Test]
    public async Task Encode_then_TryDecode_round_trips_date_and_id()
    {
        // Arrange
        var original = new EventCursor(new DateOnly(2024, 9, 29), Guid.Parse("6f1a2b3c-4d5e-6f70-8192-a3b4c5d6e7f8"));

        // Act
        var encoded = original.Encode();
        var decoded = EventCursor.TryDecode(encoded, out var cursor);

        // Assert
        await Assert.That(decoded).IsTrue();
        await Assert.That(cursor).IsEqualTo(original);
    }

    [Test]
    public async Task Encode_produces_url_safe_token_without_padding_characters()
    {
        // Arrange
        var original = new EventCursor(new DateOnly(2026, 1, 1), Guid.NewGuid());

        // Act
        var encoded = original.Encode();

        // Assert — base64url alphabet only: no '+', '/', or '='
        await Assert.That(encoded.Contains('+')).IsFalse();
        await Assert.That(encoded.Contains('/')).IsFalse();
        await Assert.That(encoded.Contains('=')).IsFalse();
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("not-base64!!!")]
    [Arguments("aGVsbG8")] // decodes to "hello" — no separator
    [Arguments("MjAyNC0wOS0yOXxub3QtYS1ndWlk")] // "2024-09-29|not-a-guid"
    [Arguments("bm90LWEtZGF0ZXw2ZjFhMmIzYy00ZDVlLTZmNzAtODE5Mi1hM2I0YzVkNmU3Zjg")] // "not-a-date|<guid>"
    public async Task TryDecode_returns_false_for_malformed_input(string? value)
    {
        // Act
        var decoded = EventCursor.TryDecode(value, out _);

        // Assert
        await Assert.That(decoded).IsFalse();
    }
}
