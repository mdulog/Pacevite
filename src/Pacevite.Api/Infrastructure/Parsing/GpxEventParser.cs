using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Pacevite.Api.Infrastructure.Parsing;

// Parses a GPX (GPS Exchange Format) track export from a device or app.
// A GPX file records only the athlete's own GPS track — it has no field for
// placement, official splits, or event type, so every parsed event is fabricated
// as GENERIC/FINISHED and flagged NeedsEnrichment for the user to fill in later.
//
// Browsers frequently send a generic content type (e.g. application/octet-stream)
// for .gpx since most OS MIME databases don't register it, so CanParse falls back
// to the filename extension rather than trusting content type alone.
public sealed class GpxEventParser : IEventParser
{
    public bool CanParse(string contentType, string fileName) =>
        contentType.Contains("gpx", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".gpx", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<ParsedEvent> Parse(Stream content)
    {
        if (content.CanSeek && content.Length == 0)
            return [];

        XDocument doc;
        try
        {
            doc = XDocument.Load(content);
        }
        catch (XmlException ex)
        {
            throw new FormatException("GPX file is not valid XML.", ex);
        }

        var trackName = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "trk")
            ?.Elements().FirstOrDefault(e => e.Name.LocalName == "name")
            ?.Value.Trim();

        if (string.IsNullOrWhiteSpace(trackName))
            throw new FormatException("GPX file is missing a required <trk><name> element.");

        var timestamps = doc.Descendants()
            .Where(e => e.Name.LocalName == "trkpt")
            .Select(trkpt => trkpt.Elements().FirstOrDefault(e => e.Name.LocalName == "time")?.Value)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => DateTimeOffset.Parse(t!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal))
            .ToList();

        if (timestamps.Count < 2)
            throw new FormatException("GPX file must contain at least two timestamped trackpoints.");

        var start = timestamps[0];
        var end = timestamps[^1];
        var elapsedSecs = (int)(end - start).TotalSeconds;

        if (elapsedSecs <= 0)
            throw new FormatException("GPX trackpoints must span a positive elapsed duration.");

        return
        [
            new ParsedEvent
            {
                EventType = "GENERIC",
                EventName = trackName,
                EventDate = DateOnly.FromDateTime(start.UtcDateTime),
                Completion = "FINISHED",
                ElapsedSecs = elapsedSecs,
                NeedsEnrichment = true,
                Source = "GPX"
            }
        ];
    }
}
