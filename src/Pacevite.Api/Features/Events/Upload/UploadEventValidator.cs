using FluentValidation;
using Pacevite.Api.Infrastructure.Parsing;

namespace Pacevite.Api.Features.Events.Upload;

public sealed class UploadEventValidator : AbstractValidator<UploadEventCommand>
{
    public UploadEventValidator(IEnumerable<IEventParser> parsers)
    {
        RuleFor(x => x.UserId).NotEmpty();

        // Delegates to the registered parsers rather than a hardcoded allowlist — adding a
        // new IEventParser automatically extends what this endpoint accepts (OCP).
        RuleFor(x => x.ContentType)
            .NotEmpty()
            .Must((command, contentType) => parsers.Any(p => p.CanParse(contentType, command.FileName)))
            .WithMessage("No parser is registered for this file type.");

        RuleFor(x => x.FileStream)
            .NotNull()
            .Must(s => s.Length > 0)
            .WithMessage("Upload file must not be empty.")
            .Must(s => s.Length <= 10 * 1024 * 1024)
            .WithMessage("Upload file must not exceed 10 MB.");
    }
}
