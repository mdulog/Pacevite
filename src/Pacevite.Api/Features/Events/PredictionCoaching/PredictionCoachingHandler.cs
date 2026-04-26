using System.Text;
using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pacevite.Api.Domain.Enums;
using Pacevite.Api.Infrastructure.Chat;
using AppSseEvent = Pacevite.Api.Infrastructure.Chat.SseEvent;
using Pacevite.Api.Infrastructure.Persistence;
using Pacevite.Api.Infrastructure.Regression;

namespace Pacevite.Api.Features.Events.PredictionCoaching;

public sealed class PredictionCoachingHandler(
    AppDbContext db,
    AnthropicClient anthropic,
    IOptions<AnthropicOptions> options)
{
    private const string SystemPrompt = """
        You are a performance coach for endurance and functional fitness events.
        Analyse the athlete's split-level trends across their race history.
        For each split, note whether it is improving, plateauing, or declining.
        Identify the 2-3 biggest opportunities for time savings.
        Be specific: name the station/segment, quantify the trend, and give one actionable coaching cue per opportunity.
        Keep the total response under 300 words. Use plain text, no markdown headers.
        """;

    public async Task HandleAsync(
        HttpContext httpContext,
        string userId,
        string eventType,
        CancellationToken ct)
    {
        if (!Enum.TryParse<EventType>(eventType, ignoreCase: true, out var parsedType))
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var events = await db.Events
            .Include(e => e.Splits.OrderBy(s => s.CumulativeSecs))
            .Where(e => e.UserId == userId
                     && e.EventType == parsedType
                     && e.Completion == CompletionStatus.Finished)
            .OrderBy(e => e.EventDate)
            .ToListAsync(ct);

        if (events.Count < 2)
        {
            httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
            return;
        }

        var firstDate = events[0].EventDate.ToDateTime(TimeOnly.MinValue);
        var points = events
            .Select(e => (
                (double)(e.EventDate.ToDateTime(TimeOnly.MinValue) - firstDate).TotalDays,
                (double)e.ElapsedSecs))
            .ToList();

        var regression    = LinearRegression.Fit(points);
        var todayDays     = (DateTime.UtcNow.Date - firstDate).TotalDays;
        var predictedSecs = (int)LinearRegression.Predict(regression, todayDays);

        var userMessage = BuildUserMessage(events, eventType, predictedSecs);

        httpContext.Response.Headers.Append("Content-Type", "text/event-stream");
        httpContext.Response.Headers.Append("Cache-Control", "no-cache");
        httpContext.Response.Headers.Append("X-Accel-Buffering", "no");

        try
        {
            var parameters = new MessageParameters
            {
                Model     = options.Value.Model,
                MaxTokens = options.Value.MaxTokens,
                Stream    = true,
                System    = [new SystemMessage(SystemPrompt)],
                Messages  = [new Message(RoleType.User, userMessage)],
            };

            await foreach (var chunk in anthropic.Messages.StreamClaudeMessageAsync(parameters, ct))
            {
                if (chunk.Delta?.Text is { } text)
                    await WriteSseAsync(httpContext.Response, AppSseEvent.Delta(text), ct);
            }

            await WriteSseAsync(httpContext.Response, AppSseEvent.Done(), ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            await WriteSseAsync(
                httpContext.Response,
                AppSseEvent.Error("Coaching analysis failed. Please try again."),
                ct);
        }
    }

    private static string BuildUserMessage(
        IReadOnlyList<Domain.Entities.Event> events,
        string eventType,
        int predictedSecs)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Here are my {eventType.ToUpperInvariant()} race results, oldest to newest:");
        sb.AppendLine();

        foreach (var ev in events)
        {
            sb.AppendLine($"{ev.EventName} — {ev.EventDate:yyyy-MM-dd} — {FormatTime(ev.ElapsedSecs)}");
            foreach (var split in ev.Splits)
                sb.AppendLine($"  {split.SplitLabel}: {FormatTime(split.SplitSecs)}");
            sb.AppendLine();
        }

        sb.AppendLine($"Algorithmic prediction for my next race: {FormatTime(predictedSecs)}");
        sb.AppendLine();
        sb.AppendLine("Please analyse my split trends and tell me where my next time savings are.");
        return sb.ToString();
    }

    private static async Task WriteSseAsync(HttpResponse response, AppSseEvent evt, CancellationToken ct)
    {
        await response.WriteAsync($"event: {evt.Type}\ndata: {evt.Data}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }

    private static string FormatTime(int secs)
    {
        int h = secs / 3600, m = (secs % 3600) / 60, s = secs % 60;
        return h > 0 ? $"{h}:{m:D2}:{s:D2}" : $"{m}:{s:D2}";
    }
}
