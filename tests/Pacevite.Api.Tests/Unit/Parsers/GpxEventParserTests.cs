using System.Text;
using Pacevite.Api.Infrastructure.Parsing;
using TUnit.Core;

namespace Pacevite.Api.Tests.Unit.Parsers;

[Category("Unit")]
public sealed class GpxEventParserTests
{
    private readonly GpxEventParser _parser = new();

    [Test]
    public async Task CanParse_ApplicationGpxXml_ReturnsTrue()
    {
        await Assert.That(_parser.CanParse("application/gpx+xml", "run.gpx")).IsTrue();
    }

    [Test]
    public async Task CanParse_OctetStreamWithGpxExtension_ReturnsTrue()
    {
        // Browsers commonly send application/octet-stream for .gpx since it has no
        // registered OS MIME mapping — the filename extension is the reliable signal.
        await Assert.That(_parser.CanParse("application/octet-stream", "morning-run.gpx")).IsTrue();
    }

    [Test]
    public async Task CanParse_TextCsv_ReturnsFalse()
    {
        await Assert.That(_parser.CanParse("text/csv", "events.csv")).IsFalse();
    }

    [Test]
    public async Task Parse_ValidGpx_ReturnsMappedEvent()
    {
        const string gpx = """
            <?xml version="1.0" encoding="UTF-8"?>
            <gpx version="1.1" creator="Garmin Connect" xmlns="http://www.topografix.com/GPX/1/1">
              <trk>
                <name>Local 10K</name>
                <trkseg>
                  <trkpt lat="52.5" lon="13.4"><ele>34.0</ele><time>2026-05-01T08:00:00Z</time></trkpt>
                  <trkpt lat="52.51" lon="13.41"><ele>36.0</ele><time>2026-05-01T08:45:00Z</time></trkpt>
                </trkseg>
              </trk>
            </gpx>
            """;

        var result = _parser.Parse(ToStream(gpx));

        await Assert.That(result.Count).IsEqualTo(1);
        var ev = result[0];
        await Assert.That(ev.EventName).IsEqualTo("Local 10K");
        await Assert.That(ev.EventType).IsEqualTo("GENERIC");
        await Assert.That(ev.Completion).IsEqualTo("FINISHED");
        await Assert.That(ev.EventDate).IsEqualTo(new DateOnly(2026, 5, 1));
        await Assert.That(ev.ElapsedSecs).IsEqualTo(2700);
        await Assert.That(ev.OverallRank).IsNull();
        await Assert.That(ev.AgeGroupRank).IsNull();
        await Assert.That(ev.FieldSize).IsNull();
        await Assert.That(ev.AgeGroupFieldSize).IsNull();
        await Assert.That(ev.NeedsEnrichment).IsTrue();
        await Assert.That(ev.Splits.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Parse_MissingTrackName_ThrowsFormatException()
    {
        const string gpx = """
            <?xml version="1.0" encoding="UTF-8"?>
            <gpx version="1.1" xmlns="http://www.topografix.com/GPX/1/1">
              <trk>
                <trkseg>
                  <trkpt lat="52.5" lon="13.4"><time>2026-05-01T08:00:00Z</time></trkpt>
                  <trkpt lat="52.51" lon="13.41"><time>2026-05-01T08:45:00Z</time></trkpt>
                </trkseg>
              </trk>
            </gpx>
            """;

        await Assert.That(() => _parser.Parse(ToStream(gpx)))
            .Throws<FormatException>();
    }

    [Test]
    public async Task Parse_FewerThanTwoTrackpoints_ThrowsFormatException()
    {
        const string gpx = """
            <?xml version="1.0" encoding="UTF-8"?>
            <gpx version="1.1" xmlns="http://www.topografix.com/GPX/1/1">
              <trk>
                <name>Local 10K</name>
                <trkseg>
                  <trkpt lat="52.5" lon="13.4"><time>2026-05-01T08:00:00Z</time></trkpt>
                </trkseg>
              </trk>
            </gpx>
            """;

        await Assert.That(() => _parser.Parse(ToStream(gpx)))
            .Throws<FormatException>();
    }

    [Test]
    public async Task Parse_MalformedXml_ThrowsFormatException()
    {
        const string gpx = "<gpx><trk><name>Broken</name>";

        await Assert.That(() => _parser.Parse(ToStream(gpx)))
            .Throws<FormatException>();
    }

    [Test]
    public async Task Parse_EmptyStream_ReturnsEmptyList()
    {
        var result = _parser.Parse(ToStream(""));

        await Assert.That(result.Count).IsEqualTo(0);
    }

    private static Stream ToStream(string content) =>
        new MemoryStream(Encoding.UTF8.GetBytes(content));
}
