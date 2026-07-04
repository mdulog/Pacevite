using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace Pacevite.Api.Features.Events.GetEvents;

// Opaque keyset-pagination cursor for GET /api/events.
// Wire format: base64url("yyyy-MM-dd|<guid>") — clients must treat it as opaque.
public readonly record struct EventCursor(DateOnly EventDate, Guid Id)
{
    private const char Separator = '|';
    private const string DateFormat = "yyyy-MM-dd";

    public string Encode()
    {
        var raw = $"{EventDate.ToString(DateFormat, CultureInfo.InvariantCulture)}{Separator}{Id}";
        return Base64Url.EncodeToString(Encoding.UTF8.GetBytes(raw));
    }

    public static bool TryDecode(string? value, out EventCursor cursor)
    {
        cursor = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        byte[] bytes;
        try
        {
            bytes = Base64Url.DecodeFromChars(value);
        }
        catch (FormatException)
        {
            // TryDecode contract: malformed input is a 'false' result, not an exception.
            return false;
        }

        var parts = Encoding.UTF8.GetString(bytes).Split(Separator);
        if (parts.Length != 2)
            return false;

        if (!DateOnly.TryParseExact(parts[0], DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return false;

        if (!Guid.TryParse(parts[1], out var id))
            return false;

        cursor = new EventCursor(date, id);
        return true;
    }
}
