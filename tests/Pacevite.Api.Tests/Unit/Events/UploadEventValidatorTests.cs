using Pacevite.Api.Features.Events.Upload;
using Pacevite.Api.Infrastructure.Parsing;
using TUnit.Core;

namespace Pacevite.Api.Tests.Unit.Events;

[Category("Unit")]
public sealed class UploadEventValidatorTests
{
    // Validates against the real registered parsers rather than a hardcoded allowlist,
    // so this exercises the exact dispatch rule the upload endpoint relies on.
    private readonly UploadEventValidator _validator = new([new CsvEventParser(), new JsonEventParser(), new GpxEventParser()]);

    private static UploadEventCommand Command(string contentType, string fileName, Stream stream) =>
        new("user-upload-validator-test", contentType, fileName, stream);

    private static Stream StreamOfLength(int bytes) => new MemoryStream(new byte[bytes]);

    [Test]
    public async Task Validate_CsvContentType_HasNoContentTypeError()
    {
        var command = Command("text/csv", "events.csv", StreamOfLength(10));

        var result = await _validator.ValidateAsync(command);

        await Assert.That(result.Errors.Any(e => e.PropertyName == nameof(UploadEventCommand.ContentType))).IsFalse();
    }

    [Test]
    public async Task Validate_GpxFilenameWithGenericContentType_HasNoContentTypeError()
    {
        // Regression case: real browsers commonly send application/octet-stream for .gpx
        // since it has no registered OS MIME mapping — the extension must still be accepted.
        var command = Command("application/octet-stream", "morning-run.gpx", StreamOfLength(10));

        var result = await _validator.ValidateAsync(command);

        await Assert.That(result.Errors.Any(e => e.PropertyName == nameof(UploadEventCommand.ContentType))).IsFalse();
    }

    [Test]
    public async Task Validate_UnrecognizedContentTypeAndExtension_HasContentTypeError()
    {
        var command = Command("application/pdf", "results.pdf", StreamOfLength(10));

        var result = await _validator.ValidateAsync(command);

        await Assert.That(result.Errors.Any(e => e.PropertyName == nameof(UploadEventCommand.ContentType))).IsTrue();
    }

    [Test]
    public async Task Validate_EmptyStream_HasFileStreamError()
    {
        var command = Command("text/csv", "events.csv", StreamOfLength(0));

        var result = await _validator.ValidateAsync(command);

        await Assert.That(result.Errors.Any(e => e.PropertyName == nameof(UploadEventCommand.FileStream))).IsTrue();
    }

    [Test]
    public async Task Validate_OversizedStream_HasFileStreamError()
    {
        var command = Command("text/csv", "events.csv", StreamOfLength(11 * 1024 * 1024));

        var result = await _validator.ValidateAsync(command);

        await Assert.That(result.Errors.Any(e => e.PropertyName == nameof(UploadEventCommand.FileStream))).IsTrue();
    }
}
