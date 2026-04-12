using System.Text.Json;

namespace Pacevite.Api.Infrastructure.Chat;

public sealed record SseEvent(string Type, string Data)
{
    public static SseEvent Delta(string text) =>
        new("delta", JsonSerializer.Serialize(new { text }));

    public static SseEvent ToolStart(string tool, string label) =>
        new("tool_start", JsonSerializer.Serialize(new { tool, label }));

    public static SseEvent ToolEnd() =>
        new("tool_end", "{}");

    public static SseEvent Done() =>
        new("done", "{}");

    public static SseEvent Error(string message) =>
        new("error", JsonSerializer.Serialize(new { message }));
}
