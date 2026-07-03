using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pacevite.Api.Features.Sync.ConnectStrava;
using Pacevite.Api.Infrastructure.Sync;
using TUnit.Core;

namespace Pacevite.Api.Tests.Unit.Sync;

[Category("Unit")]
public sealed class ConnectStravaHandlerTests
{
    private readonly ConnectStravaHandler _handler = new(
        Options.Create(new StravaOptions
        {
            ClientId = "test-client-id",
            ClientSecret = "test-client-secret",
            RedirectUri = "https://pacevite.example.com/api/sync/strava/callback"
        }),
        new EphemeralDataProtectionProvider(),
        NullLogger<ConnectStravaHandler>.Instance);

    [Test]
    public async Task Handle_ReturnsUrlPointingAtStravaAuthorizeEndpoint()
    {
        var url = await _handler.Handle(new ConnectStravaQuery("user-connect-strava-test"), CancellationToken.None);

        await Assert.That(url).StartsWith("https://www.strava.com/oauth/authorize?");
    }

    [Test]
    public async Task Handle_IncludesConfiguredClientIdAndRedirectUri()
    {
        var url = await _handler.Handle(new ConnectStravaQuery("user-connect-strava-test"), CancellationToken.None);

        await Assert.That(url).Contains("client_id=test-client-id");

        var redirectUri = System.Web.HttpUtility.ParseQueryString(new Uri(url).Query)["redirect_uri"];
        await Assert.That(redirectUri).IsEqualTo("https://pacevite.example.com/api/sync/strava/callback");
    }

    [Test]
    public async Task Handle_RequestsActivityReadAllScope()
    {
        var url = await _handler.Handle(new ConnectStravaQuery("user-connect-strava-test"), CancellationToken.None);

        await Assert.That(url).Contains("scope=activity%3Aread_all");
    }

    [Test]
    public async Task Handle_DifferentUsers_ProduceDifferentSignedState()
    {
        var urlA = await _handler.Handle(new ConnectStravaQuery("user-a"), CancellationToken.None);
        var urlB = await _handler.Handle(new ConnectStravaQuery("user-b"), CancellationToken.None);

        var stateA = ExtractStateParam(urlA);
        var stateB = ExtractStateParam(urlB);

        await Assert.That(stateA).IsNotEqualTo(stateB);
    }

    private static string ExtractStateParam(string url)
    {
        var query = System.Web.HttpUtility.ParseQueryString(new Uri(url).Query);
        return query["state"]!;
    }
}
