using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
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
public sealed class GetEventsPaginationTests
{
    private PostgreSqlContainer _postgres = null!;
    private HttpClient _client = null!;
    private WebApplicationFactory<Program> _factory = null!;

    [Before(Test)]
    public async Task SetUpAsync()
    {
        _postgres = new PostgreSqlBuilder("postgres:17")
            .WithDatabase("pacevite_pagination_test")
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

    [After(Test)]
    public async Task TearDownAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private async Task<string> GetTokenAsync(string email)
    {
        var regResponse = await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, "P@ssword1!"));
        var body = await regResponse.Content.ReadFromJsonAsync<AuthResponse>();
        return body!.Token;
    }

    private async Task UploadCsvAsync(string csv)
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(fileContent, "file", "events.csv");
        var response = await _client.PostAsync("/api/events/upload", content);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    // 5 marathons on distinct descending-friendly dates.
    private const string FiveEventsCsv = """
        MARATHON,Race One,2024-01-10,FINISHED,15000
        MARATHON,Race Two,2024-03-10,FINISHED,14800
        MARATHON,Berlin Marathon,2024-06-10,FINISHED,14600
        MARATHON,Race Four,2024-09-10,FINISHED,14400
        MARATHON,Race Five,2024-12-10,FINISHED,14200
        """;

    [Test]
    public async Task page_walk_visits_every_event_exactly_once_in_descending_date_order()
    {
        // Arrange
        var token = await GetTokenAsync("walk@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await UploadCsvAsync(FiveEventsCsv);

        // Act — walk with limit=2 until nextCursor is null
        var seen = new List<EventSummaryResponse>();
        string? cursor = null;
        do
        {
            var url = cursor is null ? "/api/events?limit=2" : $"/api/events?limit=2&cursor={cursor}";
            var page = await _client.GetFromJsonAsync<PagedEventsResponse>(url);
            seen.AddRange(page!.Items);
            cursor = page.NextCursor;
        } while (cursor is not null);

        // Assert
        await Assert.That(seen.Count).IsEqualTo(5);
        await Assert.That(seen.Select(e => e.Id).Distinct().Count()).IsEqualTo(5);
        await Assert.That(seen[0].EventName).IsEqualTo("Race Five");
        await Assert.That(seen[4].EventName).IsEqualTo("Race One");
    }

    [Test]
    public async Task inserting_an_older_event_between_pages_causes_no_skip_or_duplicate()
    {
        // Arrange — the reason keyset was chosen over OFFSET
        var token = await GetTokenAsync("stability@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await UploadCsvAsync(FiveEventsCsv);

        // Act — fetch page 1, then backfill an OLD event (like a Strava import), then continue
        var page1 = await _client.GetFromJsonAsync<PagedEventsResponse>("/api/events?limit=2");
        await UploadCsvAsync("MARATHON,Backfilled Race,2023-05-05,FINISHED,15500");
        var seen = new List<EventSummaryResponse>(page1!.Items);
        var cursor = page1.NextCursor;
        while (cursor is not null)
        {
            var page = await _client.GetFromJsonAsync<PagedEventsResponse>($"/api/events?limit=2&cursor={cursor}");
            seen.AddRange(page!.Items);
            cursor = page.NextCursor;
        }

        // Assert — all 5 originals exactly once, plus the backfill (older than the cursor position, so included)
        await Assert.That(seen.Select(e => e.Id).Distinct().Count()).IsEqualTo(seen.Count);
        await Assert.That(seen.Count).IsEqualTo(6);
        await Assert.That(seen.Any(e => e.EventName == "Backfilled Race")).IsTrue();
    }

    [Test]
    public async Task page_walk_disambiguates_events_sharing_a_date()
    {
        // Arrange — all rows share EventDate, so the keyset predicate's Id tiebreaker
        // (e.Id.CompareTo(cursor.Id) < 0) is the only thing that can prevent a skip or duplicate.
        var token = await GetTokenAsync("sameday@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await UploadCsvAsync("""
            MARATHON,Same Day A,2024-05-05,FINISHED,15000
            MARATHON,Same Day B,2024-05-05,FINISHED,14000
            MARATHON,Same Day C,2024-05-05,FINISHED,13000
            """);

        // Act — walk one row at a time so every cursor boundary falls within the same date
        var seen = new List<EventSummaryResponse>();
        string? cursor = null;
        do
        {
            var url = cursor is null ? "/api/events?limit=1" : $"/api/events?limit=1&cursor={cursor}";
            var page = await _client.GetFromJsonAsync<PagedEventsResponse>(url);
            seen.AddRange(page!.Items);
            cursor = page.NextCursor;
        } while (cursor is not null);

        // Assert — no-skip / no-duplicate is the keyset invariant; the Guid tiebreaker order
        // itself is opaque and must not be asserted as a specific sequence.
        await Assert.That(seen.Count).IsEqualTo(3);
        await Assert.That(seen.Select(e => e.Id).Distinct().Count()).IsEqualTo(3);
    }

    [Test]
    public async Task search_matches_event_name_case_insensitively()
    {
        // Arrange
        var token = await GetTokenAsync("search@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await UploadCsvAsync(FiveEventsCsv);

        // Act
        var page = await _client.GetFromJsonAsync<PagedEventsResponse>("/api/events?search=berlin");

        // Assert
        await Assert.That(page!.Items.Count).IsEqualTo(1);
        await Assert.That(page.Items[0].EventName).IsEqualTo("Berlin Marathon");
    }

    [Test]
    public async Task search_treats_like_wildcards_as_literals()
    {
        // Arrange
        var token = await GetTokenAsync("wildcard@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await UploadCsvAsync("""
            MARATHON,50% Effort Run,2024-02-01,FINISHED,16000
            MARATHON,Full Effort Run,2024-02-02,FINISHED,15000
            """);

        // Act — '%' must match only the literal percent sign, not act as a wildcard
        var page = await _client.GetFromJsonAsync<PagedEventsResponse>("/api/events?search=50%25");

        // Assert
        await Assert.That(page!.Items.Count).IsEqualTo(1);
        await Assert.That(page.Items[0].EventName).IsEqualTo("50% Effort Run");
    }

    [Test]
    public async Task search_composes_with_event_type_filter_and_cursor()
    {
        // Arrange
        var token = await GetTokenAsync("compose@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await UploadCsvAsync("""
            MARATHON,City Run A,2024-01-10,FINISHED,15000
            MARATHON,City Run B,2024-03-10,FINISHED,14800
            MARATHON,City Run C,2024-06-10,FINISHED,14600
            HYROX,City Run HYROX,2024-07-10,FINISHED,5400
            """);

        // Act — filter+search page 1 then page 2
        var page1 = await _client.GetFromJsonAsync<PagedEventsResponse>("/api/events?eventType=MARATHON&search=City&limit=2");
        var page2 = await _client.GetFromJsonAsync<PagedEventsResponse>($"/api/events?eventType=MARATHON&search=City&limit=2&cursor={page1!.NextCursor}");

        // Assert
        await Assert.That(page1.Items.Count).IsEqualTo(2);
        await Assert.That(page2!.Items.Count).IsEqualTo(1);
        await Assert.That(page2.NextCursor).IsNull();
        await Assert.That(page1.Items.Concat(page2.Items).All(e => e.EventType == "MARATHON")).IsTrue();
    }

    [Test]
    public async Task limit_above_maximum_returns_400()
    {
        // Arrange
        var token = await GetTokenAsync("limit@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.GetAsync("/api/events?limit=101");

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task malformed_cursor_returns_400()
    {
        // Arrange
        var token = await GetTokenAsync("cursor@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.GetAsync("/api/events?cursor=garbage!!!");

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task list_items_do_not_include_splits_property()
    {
        // Arrange
        var token = await GetTokenAsync("splitless@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await UploadCsvAsync("MARATHON,Splitless Check,2024-01-10,FINISHED,15000");

        // Act
        var raw = await _client.GetStringAsync("/api/events");

        // Assert — the summary payload must not carry the splits array at all
        await Assert.That(raw.Contains("\"splits\"")).IsFalse();
    }

    [Test]
    public async Task pagination_is_scoped_to_the_authenticated_user()
    {
        // Arrange — user A has events; user B must see an empty page, not A's data
        var tokenA = await GetTokenAsync("owner-a@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        await UploadCsvAsync(FiveEventsCsv);

        var tokenB = await GetTokenAsync("other-b@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);

        // Act
        var page = await _client.GetFromJsonAsync<PagedEventsResponse>("/api/events");

        // Assert
        await Assert.That(page!.Items.Count).IsEqualTo(0);
        await Assert.That(page.NextCursor).IsNull();
    }
}
