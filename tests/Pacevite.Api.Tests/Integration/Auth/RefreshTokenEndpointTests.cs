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
using TUnit.Assertions;
using TUnit.Core;

namespace Pacevite.Api.Tests.Integration.Auth;

[Category("Integration")]
public sealed class RefreshTokenEndpointTests
{
    private PostgreSqlContainer _postgres = null!;
    private HttpClient _client = null!;
    private WebApplicationFactory<Program> _factory = null!;

    [Before(Test)]
    public async Task SetUpAsync()
    {
        _postgres = new PostgreSqlBuilder("postgres:17")
            .WithDatabase("pacevite_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();
        await _postgres.StartAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                if (descriptor is not null) services.Remove(descriptor);
                services.AddDbContext<AppDbContext>(opts => opts.UseNpgsql(_postgres.GetConnectionString()));

                // Apply migrations against the test DB
                using var scope = services.BuildServiceProvider().CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.Database.Migrate();
            });
            host.UseSetting("Jwt:Secret", "super-secret-key-for-testing-only-32c");
            host.UseSetting("Jwt:Issuer", "pacevite-test");
            host.UseSetting("Jwt:Audience", "pacevite-test");
            host.UseSetting("Jwt:AccessTokenExpiryMinutes", "15");
        });

        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
    }

    [After(Test)]
    public async Task TearDownAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private async Task<(string AccessToken, string RawRefreshToken)> RegisterAndGetTokensAsync(string email)
    {
        await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, "Password123!"));
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "Password123!"));
        var auth = await resp.Content.ReadFromJsonAsync<AuthResponse>();
        var cookieHeader = resp.Headers.GetValues("Set-Cookie").First(c => c.StartsWith("refreshToken="));
        var rawRefreshToken = cookieHeader.Split(';')[0].Split('=', 2)[1];
        return (auth!.Token, rawRefreshToken);
    }

    [Test]
    public async Task login_sets_httponly_refresh_token_cookie()
    {
        // Arrange
        await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("cookie-login@example.com", "Password123!"));

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest("cookie-login@example.com", "Password123!"));

        // Assert
        var cookies = response.Headers.GetValues("Set-Cookie").ToList();
        var refreshCookie = cookies.FirstOrDefault(c => c.Contains("refreshToken="));
        await Assert.That(refreshCookie).IsNotNull();
        await Assert.That(refreshCookie).Contains("httponly", StringComparison.OrdinalIgnoreCase);
        await Assert.That(refreshCookie).Contains("path=/api/auth", StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public async Task register_sets_httponly_refresh_token_cookie()
    {
        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("cookie-register@example.com", "Password123!"));

        // Assert
        var cookies = response.Headers.GetValues("Set-Cookie").ToList();
        var refreshCookie = cookies.FirstOrDefault(c => c.Contains("refreshToken="));
        await Assert.That(refreshCookie).IsNotNull();
        await Assert.That(refreshCookie).Contains("httponly", StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public async Task refresh_returns_new_access_token_when_cookie_valid()
    {
        // Arrange
        var (_, rawRefreshToken) = await RegisterAndGetTokensAsync("refresh-valid@example.com");
        _client.DefaultRequestHeaders.Add("Cookie", $"refreshToken={rawRefreshToken}");

        // Act
        var response = await _client.PostAsync("/api/auth/refresh", null);
        var body = await response.Content.ReadFromJsonAsync<RefreshResponse>();

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(body!.Token).IsNotNull();
        await Assert.That(body.Token).IsNotEmpty();
    }

    [Test]
    public async Task refresh_rotates_cookie_to_new_value()
    {
        // Arrange
        var (_, rawRefreshToken) = await RegisterAndGetTokensAsync("refresh-rotate@example.com");
        _client.DefaultRequestHeaders.Add("Cookie", $"refreshToken={rawRefreshToken}");

        // Act
        var response = await _client.PostAsync("/api/auth/refresh", null);

        // Assert
        var cookies = response.Headers.GetValues("Set-Cookie").ToList();
        var newCookie = cookies.FirstOrDefault(c => c.Contains("refreshToken="));
        await Assert.That(newCookie).IsNotNull();
        await Assert.That(newCookie).DoesNotContain(rawRefreshToken);
    }

    [Test]
    public async Task refresh_returns_401_when_cookie_missing()
    {
        // Act
        var response = await _client.PostAsync("/api/auth/refresh", null);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task refresh_returns_401_when_token_already_rotated()
    {
        // Arrange — use the token once to rotate it
        var (_, rawRefreshToken) = await RegisterAndGetTokensAsync("refresh-reuse@example.com");
        _client.DefaultRequestHeaders.Add("Cookie", $"refreshToken={rawRefreshToken}");
        await _client.PostAsync("/api/auth/refresh", null);

        // Act — attempt to reuse the original (now revoked) token
        var response = await _client.PostAsync("/api/auth/refresh", null);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task logout_revokes_token_so_subsequent_refresh_returns_401()
    {
        // Arrange
        var (accessToken, rawRefreshToken) = await RegisterAndGetTokensAsync("logout-revoke@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        _client.DefaultRequestHeaders.Add("Cookie", $"refreshToken={rawRefreshToken}");

        // Act
        var logoutResponse = await _client.PostAsync("/api/auth/logout", null);

        // Assert logout succeeded
        await Assert.That(logoutResponse.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

        // Assert revoked token no longer works
        var refreshResponse = await _client.PostAsync("/api/auth/refresh", null);
        await Assert.That(refreshResponse.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task logout_returns_204_when_no_refresh_cookie()
    {
        // Arrange
        var (accessToken, _) = await RegisterAndGetTokensAsync("logout-nocookie@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        // Act
        var response = await _client.PostAsync("/api/auth/logout", null);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task logout_requires_authorization()
    {
        // Act — no Bearer token
        var response = await _client.PostAsync("/api/auth/logout", null);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }
}
