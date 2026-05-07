---
name: add-chat-tool
description: Scaffold a new IChatToolHandler, register it in DI, and generate its unit test. Takes a tool name (snake_case) and a description of what it does.
---

Follow the pattern established in `src/Pacevite.Api/Infrastructure/Chat/` and `tests/Pacevite.Api.Tests/Unit/Chat/ChatToolExecutorTests.cs`.

## Files to create

**Handler** (`src/Pacevite.Api/Infrastructure/Chat/Tools/{PascalName}Handler.cs`):
```csharp
public sealed class {PascalName}Handler(/* inject repos/services */) : IChatToolHandler
{
    public async ValueTask<string> ExecuteAsync(JsonNode input, string userId, CancellationToken ct)
    {
        // 1. Parse required fields from input (JsonNode) — throw ArgumentException if missing
        // 2. Call the relevant repository or service
        // 3. Return a JSON string summarising the result
    }
}
```

**Unit test** (`tests/Pacevite.Api.Tests/Unit/Chat/{PascalName}HandlerTests.cs`):
- `[Category("Unit")]`
- Test: tool dispatched via `ChatToolExecutor` returns expected result
- Test: unknown tool name returns an error string (copy pattern from `ChatToolExecutorTests`)
- Test: missing required input field throws or returns a descriptive error

## Wiring in Program.cs

Locate the `IReadOnlyDictionary<string, IChatToolHandler>` registration passed to `ChatToolExecutor` and add:
```csharp
["{tool_name}"] = new {PascalName}Handler(/* resolve deps from sp */)
```

## Notes
- Tool names must be snake_case (e.g. `get_personal_bests`)
- Input parsing should be defensive — the model may omit optional fields
- Return value is always a plain string; use `JsonSerializer.Serialize` for structured results
