---
name: add-parser
description: Scaffold a new IEventParser for a file format, register it, and generate its unit tests. Takes a format name and MIME content type.
---

Follow the pattern in `src/Pacevite.Api/Infrastructure/Parsing/` using `CsvEventParser` and `JsonEventParser` as reference.

## Files to create

**Parser** (`src/Pacevite.Api/Infrastructure/Parsing/{Format}EventParser.cs`):
```csharp
public sealed class {Format}EventParser : IEventParser
{
    public bool CanParse(string contentType) =>
        contentType.StartsWith("{mime-type}", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<ParsedEvent> Parse(Stream content)
    {
        // Read the stream and map to List<ParsedEvent>
        // Throw FormatException for missing required fields (event_type, event_name, event_date, completion, elapsed_secs)
        // Return empty list for empty input — never return null
    }
}
```

**Unit tests** (`tests/Pacevite.Api.Tests/Unit/Parsers/{Format}EventParserTests.cs`):
- `[Category("Unit")]`
- `CanParse` returns true for the target MIME type
- `CanParse` returns false for unrelated types (e.g. `text/csv`, `application/json`)
- `Parse` with valid input returns correctly mapped `ParsedEvent` list
- `Parse` with missing required field throws `FormatException`
- `Parse` with empty/minimal input returns empty list
- `Parse` with optional rank fields populates them correctly

## Wiring in Program.cs

Add alongside the existing parser registrations:
```csharp
builder.Services.AddSingleton<IEventParser, {Format}EventParser>();
```

## Notes
- `ParsedEvent` properties: `EventType`, `EventName`, `EventDate`, `Completion`, `ElapsedSecs`, `OverallRank?`, `AgeGroupRank?`, `FieldSize?`, `AgeGroupFieldSize?`, `Location`, `Metadata`, `Splits`
- Values for `EventType` and `Completion` must be uppercased — normalise in the parser, not the handler
- `Splits` maps to `List<ParsedSplit>` with `SplitType`, `SplitLabel`, `SplitSecs`, `CumulativeSecs`, `Metadata`
