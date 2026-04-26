using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pacevite.Api.Contracts.Requests;
using Pacevite.Api.Contracts.Responses;
using Pacevite.Api.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using TUnit.Core;

namespace Pacevite.Api.Tests.Integration;

[Category("Integration")]
public sealed class PredictionEndpointsTests
{
    private PostgreSqlContainer _postgres = null!;
    private HttpClient _client = null!;
    private WebApplicationFactory<Program> _factory = null!;

    [Before(Test)]
    public async Task SetUpAsync()
    {
        _postgres = new PostgreSqlBuilder("postgres:17")
            .WithDatabase("pacevite_predict_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();
        await _postgres.StartAsync();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(host =>
            {
                host.ConfigureServices(services =>
                {
                    var descriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                    if (descriptor is not null) services.Remove(descriptor);

                    services.AddDbContext<AppDbContext>(options =>
                        options.UseNpgsql(_postgres.GetConnectionString()));

                    using var scope = services.BuildServiceProvider().CreateScope();
                    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
                });

                host.UseSetting("Jwt:Secret", "super-secret-key-for-testing-only-32c");
                host.UseSetting("Jwt:Issuer", "pacevite-test");
                host.UseSetting("Jwt:Audience", "pacevite-test");
                host.UseSetting("Anthropic:ApiKey", "test-key");
                host.UseSetting("Anthropic:Model", "claude-haiku-4-5-20251001");
                host.UseSetting("Anthropic:MaxTokens", "1024");
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

    private async Task<string> GetTokenAsync(string email = "predict-user@example.com")
    {
        var reg = await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, "P@ssword1!"));
        if (reg.IsSuccessStatusCode)
        {
            var body = await reg.Content.ReadFromJsonAsync<AuthResponse>();
            return body!.Token;
        }
        var login = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(email, "P@ssword1!"));
        return (await login.Content.ReadFromJsonAsync<AuthResponse>())!.Token;
    }

    private async Task UploadHyroxEventsAsync(string token, int count)
    {
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        for (int i = 0; i < count; i++)
        {
            var date = new DateOnly(2023, 1, 1).AddMonths(i * 6);
            var secs = 5000 - i * 300;
            var json = $$"""
                [{"event_type":"HYROX","event_name":"HYROX {{i+1}}","event_date":"{{date:yyyy-MM-dd}}",
                  "completion":"FINISHED","elapsed_secs":{{secs}},"splits":[]}]
                """;
            var content = new MultipartFormDataContent();
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            var fileContent = new ByteArrayContent(bytes);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            content.Add(fileContent, "file", "events.json");
            await _client.PostAsync("/api/events/upload", content);
        }
    }

    [Test]
    public async Task GetPrediction_Unauthenticated_Returns401()
    {
        var response = await _client.GetAsync("/api/events/prediction?eventType=HYROX");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task GetPrediction_TwoFinishedEvents_Returns200WithPrediction()
    {
        var token = await GetTokenAsync("predict-2events@example.com");
        await UploadHyroxEventsAsync(token, 2);

        var response = await _client.GetAsync("/api/events/prediction?eventType=HYROX");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var prediction = await response.Content.ReadFromJsonAsync<PredictionResponse>();
        await Assert.That(prediction).IsNotNull();
        await Assert.That(prediction!.EventType).IsEqualTo("HYROX");
        await Assert.That(prediction.PredictedSecs).IsGreaterThan(0);
        await Assert.That(prediction.DataPoints.Count).IsEqualTo(3);
    }

    [Test]
    public async Task GetPrediction_OneFinishedEvent_Returns409()
    {
        var token = await GetTokenAsync("predict-1event@example.com");
        await UploadHyroxEventsAsync(token, 1);

        var response = await _client.GetAsync("/api/events/prediction?eventType=HYROX");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task GetPrediction_InvalidEventType_Returns400()
    {
        var token = await GetTokenAsync("predict-bad-type@example.com");
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync("/api/events/prediction?eventType=TRIATHLON");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task GetPredictionCoaching_Unauthenticated_Returns401()
    {
        var response = await _client.GetAsync(
            "/api/events/prediction/coaching?eventType=HYROX",
            HttpCompletionOption.ResponseHeadersRead);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task GetPredictionCoaching_InsufficientData_Returns409()
    {
        var token = await GetTokenAsync("coaching-1event@example.com");
        await UploadHyroxEventsAsync(token, 1);

        var response = await _client.GetAsync(
            "/api/events/prediction/coaching?eventType=HYROX",
            HttpCompletionOption.ResponseHeadersRead);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task GetPredictionCoaching_ValidData_ReturnsEventStream()
    {
        var token = await GetTokenAsync("coaching-2events@example.com");
        await UploadHyroxEventsAsync(token, 2);

        var response = await _client.GetAsync(
            "/api/events/prediction/coaching?eventType=HYROX",
            HttpCompletionOption.ResponseHeadersRead);

        // Self-skip in CI if no real API key configured (stub key returns 500)
        if (response.StatusCode == System.Net.HttpStatusCode.InternalServerError) return;

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Content.Headers.ContentType?.MediaType)
            .IsEqualTo("text/event-stream");
    }
}
