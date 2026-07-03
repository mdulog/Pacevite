using Microsoft.AspNetCore.DataProtection;
using Pacevite.Api.Infrastructure.Sync;
using TUnit.Core;

namespace Pacevite.Api.Tests.Unit.Sync;

[Category("Unit")]
public sealed class StravaStateTests
{
    private readonly IDataProtectionProvider _provider = new EphemeralDataProtectionProvider();

    [Test]
    public async Task TryUnprotect_ValidState_ReturnsOriginalUserId()
    {
        var state = StravaState.Create(_provider, "user-strava-state-test");

        var result = StravaState.TryUnprotect(_provider, state);

        await Assert.That(result).IsEqualTo("user-strava-state-test");
    }

    [Test]
    public async Task TryUnprotect_TamperedState_ReturnsNull()
    {
        var state = StravaState.Create(_provider, "user-strava-state-test");
        var tampered = state[..^1] + (state[^1] == 'a' ? 'b' : 'a');

        var result = StravaState.TryUnprotect(_provider, tampered);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task TryUnprotect_StateFromDifferentProvider_ReturnsNull()
    {
        var otherProvider = new EphemeralDataProtectionProvider();
        var state = StravaState.Create(otherProvider, "user-strava-state-test");

        var result = StravaState.TryUnprotect(_provider, state);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task TryUnprotect_GarbageInput_ReturnsNull()
    {
        var result = StravaState.TryUnprotect(_provider, "not-a-real-protected-value");

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task TryUnprotect_ExpiredState_ReturnsNull()
    {
        var createdAt = new FixedTimeProvider(new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero));
        var state = StravaState.Create(_provider, "user-strava-state-test", createdAt);

        var elevenMinutesLater = new FixedTimeProvider(createdAt.GetUtcNow().AddMinutes(11));
        var result = StravaState.TryUnprotect(_provider, state, elevenMinutesLater);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task TryUnprotect_StateJustBeforeExpiry_ReturnsUserId()
    {
        var createdAt = new FixedTimeProvider(new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero));
        var state = StravaState.Create(_provider, "user-strava-state-test", createdAt);

        var nineMinutesLater = new FixedTimeProvider(createdAt.GetUtcNow().AddMinutes(9));
        var result = StravaState.TryUnprotect(_provider, state, nineMinutesLater);

        await Assert.That(result).IsEqualTo("user-strava-state-test");
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
