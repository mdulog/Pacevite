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
public sealed class CreateEventTests : IAsyncDisposable
{
    private readonly PostgreSqlContainer _postgres;
    private readonly HttpClient _client;
    private readonly WebApplicationFactory<Program> _factory;

    public CreateEventTests()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:17")
            .WithDatabase("pacevite_createevent_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();

        _postgres.StartAsync().GetAwaiter().GetResult();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(host =>
            {
                host.ConfigureServices(services =>
                {
                    var descriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                    if (descriptor is not null)
                        services.Remove(descriptor);

                    services.AddDbContext<AppDbContext>(options =>
                        options.UseNpgsql(_postgres.GetConnectionString()));

                    using var scope = services.BuildServiceProvider().CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    db.Database.Migrate();
                });

                host.UseSetting("Jwt:Secret", "super-secret-key-for-testing-only-32c");
                host.UseSetting("Jwt:Issuer", "pacevite-test");
                host.UseSetting("Jwt:Audience", "pacevite-test");
            });

        _client = _factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private async Task<string> GetTokenAsync(string email)
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
        var loginBody = await login.Content.ReadFromJsonAsync<AuthResponse>();
        return loginBody!.Token;
    }

    private static CreateEventRequest BuildValidRequest(string eventName = "Local 10K", DateOnly? date = null) => new(
        EventType: "GENERIC",
        EventName: eventName,
        EventDate: date ?? new DateOnly(2026, 5, 1),
        Completion: "FINISHED",
        ElapsedSecs: 2700,
        OverallRank: 42,
        AgeGroupRank: 5,
        FieldSize: 500,
        AgeGroupFieldSize: 60,
        Splits: null);

    [Test]
    public async Task CreateEvent_Returns201WithEvent_WhenRequestValid()
    {
        // Arrange
        var token = await GetTokenAsync("create-event-valid@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var request = BuildValidRequest();

        // Act
        var response = await _client.PostAsJsonAsync("/api/events", request);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<EventResponse>();
        await Assert.That(created!.EventName).IsEqualTo("Local 10K");
        await Assert.That(created.EventType).IsEqualTo("GENERIC");
        await Assert.That(created.ElapsedSecs).IsEqualTo(2700);
        await Assert.That(created.Source).IsEqualTo("MANUAL");
    }

    [Test]
    public async Task CreateEvent_Returns409_WhenDuplicateEventExists()
    {
        // Arrange
        var token = await GetTokenAsync("create-event-duplicate@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var request = BuildValidRequest(eventName: "Duplicate 10K", date: new DateOnly(2026, 3, 15));

        var first = await _client.PostAsJsonAsync("/api/events", request);
        await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.Created);

        // Act — same user, same type/name/date submitted again
        var second = await _client.PostAsJsonAsync("/api/events", request);

        // Assert
        await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task CreateEvent_Returns400_WhenEventTypeInvalid()
    {
        // Arrange
        var token = await GetTokenAsync("create-event-badtype@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var request = BuildValidRequest() with { EventType = "TRIATHLON" };

        // Act
        var response = await _client.PostAsJsonAsync("/api/events", request);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task CreateEvent_Returns401_WhenNotAuthenticated()
    {
        // Act
        var response = await _client.PostAsJsonAsync("/api/events", BuildValidRequest());

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }
}
