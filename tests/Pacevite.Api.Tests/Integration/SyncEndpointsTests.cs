using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pacevite.Api.Contracts.Requests;
using Pacevite.Api.Contracts.Responses;
using Pacevite.Api.Infrastructure.Persistence;
using Pacevite.Api.Infrastructure.Sync;
using Pacevite.Api.Tests.Unit.Sync;
using Testcontainers.PostgreSql;
using TUnit.Core;

namespace Pacevite.Api.Tests.Integration;

[Category("Integration")]
public sealed class SyncEndpointsTests
{
    private PostgreSqlContainer _postgres = null!;
    private HttpClient _client = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private FakeStravaClient _stravaClient = null!;

    [Before(Test)]
    public async Task SetUpAsync()
    {
        _postgres = new PostgreSqlBuilder("postgres:17")
            .WithDatabase("pacevite_sync_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();

        await _postgres.StartAsync();

        _stravaClient = new FakeStravaClient();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(host =>
            {
                host.ConfigureServices(services =>
                {
                    var dbDescriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                    if (dbDescriptor is not null)
                        services.Remove(dbDescriptor);

                    services.AddDbContext<AppDbContext>(options =>
                        options.UseNpgsql(_postgres.GetConnectionString()));

                    // Replace the real HTTP-backed Strava client with a fake — this is the
                    // external boundary, so integration tests mock it rather than hitting Strava.
                    var stravaDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IStravaClient));
                    if (stravaDescriptor is not null)
                        services.Remove(stravaDescriptor);
                    services.AddSingleton<IStravaClient>(_stravaClient);

                    using var scope = services.BuildServiceProvider().CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    db.Database.Migrate();
                });

                host.UseSetting("Jwt:Secret", "super-secret-key-for-testing-only-32c");
                host.UseSetting("Jwt:Issuer", "pacevite-test");
                host.UseSetting("Jwt:Audience", "pacevite-test");
                host.UseSetting("Strava:ClientId", "test-client-id");
                host.UseSetting("Strava:ClientSecret", "test-client-secret");
                host.UseSetting("Strava:RedirectUri", "https://pacevite-test.example.com/api/sync/strava/callback");
            });

        _client = _factory.CreateClient();
    }

    [After(Test)]
    public async Task TearDownAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private async Task<string> GetTokenAsync(string email)
    {
        var reg = await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, "P@ssword1!"));
        if (reg.IsSuccessStatusCode)
        {
            var body = await reg.Content.ReadFromJsonAsync<AuthResponse>();
            return body!.Token;
        }

        var login = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "P@ssword1!"));
        var loginBody = await login.Content.ReadFromJsonAsync<AuthResponse>();
        return loginBody!.Token;
    }

    private async Task<string> GetSignedStateAsync(string token)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.GetAsync("/api/sync/strava/connect");
        var body = await response.Content.ReadFromJsonAsync<ConnectStravaResponse>();
        var query = HttpUtility.ParseQueryString(new Uri(body!.AuthorizeUrl).Query);
        return query["state"]!;
    }

    [Test]
    public async Task ConnectStrava_WithoutToken_Returns401()
    {
        var response = await _client.GetAsync("/api/sync/strava/connect");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task ConnectStrava_Authenticated_ReturnsAuthorizeUrl()
    {
        var token = await GetTokenAsync("sync-connect@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync("/api/sync/strava/connect");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ConnectStravaResponse>();
        await Assert.That(body!.AuthorizeUrl).StartsWith("https://www.strava.com/oauth/authorize?");
    }

    [Test]
    public async Task StravaCallback_ValidState_RedirectsToSyncConnectedTrue()
    {
        var token = await GetTokenAsync("sync-callback-valid@example.com");
        var state = await GetSignedStateAsync(token);

        using var redirectClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await redirectClient.GetAsync($"/api/sync/strava/callback?code=auth-code-123&state={Uri.EscapeDataString(state)}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        await Assert.That(response.Headers.Location!.ToString()).IsEqualTo("/sync?connected=true");
    }

    [Test]
    public async Task StravaCallback_ForgedState_RedirectsToSyncConnectedFalse()
    {
        using var redirectClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await redirectClient.GetAsync("/api/sync/strava/callback?code=auth-code-123&state=forged-state-value");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        await Assert.That(response.Headers.Location!.ToString()).IsEqualTo("/sync?connected=false");
    }

    [Test]
    public async Task GetActivities_WithoutConnection_Returns409()
    {
        var token = await GetTokenAsync("sync-activities-noconn@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync("/api/sync/strava/activities");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task GetActivities_AfterConnecting_ReturnsMappedActivities()
    {
        var token = await GetTokenAsync("sync-activities@example.com");
        var state = await GetSignedStateAsync(token);

        using var redirectClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await redirectClient.GetAsync($"/api/sync/strava/callback?code=auth-code-123&state={Uri.EscapeDataString(state)}");

        _stravaClient.ActivitiesToReturn =
        [
            new StravaActivity { Id = 555, Name = "Trail Race", Type = "Run", StartDate = new DateTimeOffset(2026, 5, 10, 8, 0, 0, TimeSpan.Zero), ElapsedTimeSecs = 4500 }
        ];

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.GetAsync("/api/sync/strava/activities");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var activities = await response.Content.ReadFromJsonAsync<List<StravaActivityPreviewResponse>>();
        await Assert.That(activities!.Count).IsEqualTo(1);
        await Assert.That(activities[0].Name).IsEqualTo("Trail Race");
        await Assert.That(activities[0].ElapsedSecs).IsEqualTo(4500);
    }

    [Test]
    public async Task ConfirmActivity_NewActivity_Returns201AndAppearsInEvents()
    {
        var token = await GetTokenAsync("sync-confirm@example.com");
        var state = await GetSignedStateAsync(token);

        using var redirectClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await redirectClient.GetAsync($"/api/sync/strava/callback?code=auth-code-123&state={Uri.EscapeDataString(state)}");

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var request = new ConfirmStravaActivityRequest("strava-activity-999", "Weekend Ride", new DateOnly(2026, 5, 9), 6000);

        var response = await _client.PostAsJsonAsync("/api/sync/strava/activities/confirm", request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<EventResponse>();
        await Assert.That(created!.Source).IsEqualTo("STRAVA");
        await Assert.That(created.NeedsEnrichment).IsTrue();

        var eventsResponse = await _client.GetAsync("/api/events");
        var events = await eventsResponse.Content.ReadFromJsonAsync<List<EventResponse>>();
        await Assert.That(events!.Any(e => e.EventName == "Weekend Ride")).IsTrue();
    }

    [Test]
    public async Task ConfirmActivity_AlreadyImported_Returns409()
    {
        var token = await GetTokenAsync("sync-confirm-dup@example.com");
        var state = await GetSignedStateAsync(token);

        using var redirectClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await redirectClient.GetAsync($"/api/sync/strava/callback?code=auth-code-123&state={Uri.EscapeDataString(state)}");

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var request = new ConfirmStravaActivityRequest("strava-activity-777", "Track Session", new DateOnly(2026, 5, 8), 3000);
        await _client.PostAsJsonAsync("/api/sync/strava/activities/confirm", request);

        var second = await _client.PostAsJsonAsync("/api/sync/strava/activities/confirm", request);

        await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task ConfirmActivity_WithoutToken_Returns401()
    {
        var request = new ConfirmStravaActivityRequest("strava-activity-1", "Test", new DateOnly(2026, 5, 1), 1000);

        var response = await _client.PostAsJsonAsync("/api/sync/strava/activities/confirm", request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }
}
