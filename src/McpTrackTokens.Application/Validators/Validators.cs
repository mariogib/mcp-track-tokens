using FluentValidation;
using McpTrackTokens.Application.DTOs;

namespace McpTrackTokens.Application.Validators;

/// <summary>
/// Validates inbound activity events.
/// </summary>
public sealed class IngestEventDtoValidator : AbstractValidator<IngestEventDto>
{
    public IngestEventDtoValidator()
    {
        RuleFor(x => x.SchemaVersion)
            .NotEmpty()
            .MaximumLength(20);

        RuleFor(x => x.EventType)
            .NotEmpty()
            .MaximumLength(64);

        RuleFor(x => x.TimestampUtc)
            .Must(ts => ts != default)
            .WithMessage("timestampUtc is required.");

        RuleFor(x => x.Editor)
            .NotEmpty()
            .MaximumLength(64);

        RuleFor(x => x.ExternalEventId)
            .MaximumLength(200)
            .When(x => x.ExternalEventId is not null);

        RuleFor(x => x.ExternalSessionId)
            .MaximumLength(200)
            .When(x => x.ExternalSessionId is not null);

        RuleFor(x => x.PromptLength)
            .GreaterThanOrEqualTo(0)
            .When(x => x.PromptLength.HasValue);

        RuleFor(x => x.DurationMilliseconds)
            .GreaterThanOrEqualTo(0)
            .When(x => x.DurationMilliseconds.HasValue);

        RuleFor(x => x.WorkspacePath)
            .MaximumLength(1024)
            .When(x => x.WorkspacePath is not null);

        RuleFor(x => x.RepositoryPath)
            .MaximumLength(1024)
            .When(x => x.RepositoryPath is not null);
    }
}

/// <summary>
/// Validates batch ingestion requests.
/// </summary>
public sealed class BatchIngestRequestDtoValidator : AbstractValidator<BatchIngestRequestDto>
{
    public BatchIngestRequestDtoValidator()
    {
        RuleFor(x => x.Events)
            .NotNull()
            .Must(e => e.Count > 0)
            .WithMessage("At least one event is required.")
            .Must(e => e.Count <= 500)
            .WithMessage("A batch may contain at most 500 events.");

        RuleForEach(x => x.Events).SetValidator(new IngestEventDtoValidator());
    }
}

/// <summary>
/// Validates project creation requests.
/// </summary>
public sealed class CreateProjectRequestValidator : AbstractValidator<CreateProjectRequest>
{
    public CreateProjectRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(x => x.Slug)
            .MaximumLength(100)
            .Matches(@"^[a-z0-9]+(?:-[a-z0-9]+)*$")
            .When(x => !string.IsNullOrWhiteSpace(x.Slug))
            .WithMessage("Slug must be lowercase letters, digits, or hyphens.");

        RuleFor(x => x.Currency)
            .Length(3)
            .Matches("^[A-Za-z]{3}$")
            .When(x => !string.IsNullOrWhiteSpace(x.Currency))
            .WithMessage("Currency must be a 3-letter code.");

        RuleFor(x => x.ClientName)
            .MaximumLength(200)
            .When(x => x.ClientName is not null);

        RuleFor(x => x.BillingCode)
            .MaximumLength(100)
            .When(x => x.BillingCode is not null);

        RuleFor(x => x.RepositoryPath)
            .MaximumLength(1024)
            .When(x => x.RepositoryPath is not null);

        RuleFor(x => x.RemoteUrl)
            .MaximumLength(1024)
            .When(x => x.RemoteUrl is not null);
    }
}

/// <summary>
/// Validates project update requests.
/// </summary>
public sealed class UpdateProjectRequestValidator : AbstractValidator<UpdateProjectRequest>
{
    public UpdateProjectRequestValidator()
    {
        RuleFor(x => x.Name)
            .MaximumLength(200)
            .When(x => !string.IsNullOrWhiteSpace(x.Name));

        RuleFor(x => x.Slug)
            .MaximumLength(100)
            .Matches(@"^[a-z0-9]+(?:-[a-z0-9]+)*$")
            .When(x => !string.IsNullOrWhiteSpace(x.Slug))
            .WithMessage("Slug must be lowercase letters, digits, or hyphens.");

        RuleFor(x => x.Currency)
            .Length(3)
            .Matches("^[A-Za-z]{3}$")
            .When(x => !string.IsNullOrWhiteSpace(x.Currency))
            .WithMessage("Currency must be a 3-letter code.");

        RuleFor(x => x.ClientName)
            .MaximumLength(200)
            .When(x => x.ClientName is not null);

        RuleFor(x => x.BillingCode)
            .MaximumLength(100)
            .When(x => x.BillingCode is not null);

        RuleFor(x => x.RepositoryPath)
            .MaximumLength(1024)
            .When(x => x.RepositoryPath is not null);

        RuleFor(x => x.RemoteUrl)
            .MaximumLength(1024)
            .When(x => x.RemoteUrl is not null);
    }
}

/// <summary>
/// Validates dashboard create-session requests.
/// </summary>
public sealed class CreateProjectSessionRequestValidator : AbstractValidator<CreateProjectSessionRequest>
{
    public CreateProjectSessionRequestValidator()
    {
        RuleFor(x => x.Editor)
            .NotEmpty()
            .MaximumLength(64);

        RuleFor(x => x.EditorVersion).MaximumLength(64).When(x => x.EditorVersion is not null);
        RuleFor(x => x.MachineName).MaximumLength(200).When(x => x.MachineName is not null);
        RuleFor(x => x.UserName).MaximumLength(200).When(x => x.UserName is not null);
        RuleFor(x => x.WorkspacePath).MaximumLength(1024).When(x => x.WorkspacePath is not null);
        RuleFor(x => x.RepositoryPath).MaximumLength(1024).When(x => x.RepositoryPath is not null);
        RuleFor(x => x.RemoteUrl).MaximumLength(1024).When(x => x.RemoteUrl is not null);
        RuleFor(x => x.Branch).MaximumLength(200).When(x => x.Branch is not null);
        RuleFor(x => x.ExternalSessionId).MaximumLength(200).When(x => x.ExternalSessionId is not null);
        RuleFor(x => x.Status).MaximumLength(32).When(x => x.Status is not null);

        RuleFor(x => x.EndedAtUtc)
            .GreaterThanOrEqualTo(x => x.StartedAtUtc!.Value)
            .When(x => x.StartedAtUtc.HasValue && x.EndedAtUtc.HasValue)
            .WithMessage("endedAtUtc cannot be earlier than startedAtUtc.");
    }
}

/// <summary>
/// Validates dashboard update-session requests.
/// </summary>
public sealed class UpdateSessionRequestValidator : AbstractValidator<UpdateSessionRequest>
{
    public UpdateSessionRequestValidator()
    {
        RuleFor(x => x.Editor)
            .NotEmpty()
            .MaximumLength(64);

        RuleFor(x => x.Status)
            .NotEmpty()
            .MaximumLength(32);

        RuleFor(x => x.StartedAtUtc)
            .Must(ts => ts != default)
            .WithMessage("startedAtUtc is required.");

        RuleFor(x => x.EditorVersion).MaximumLength(64).When(x => x.EditorVersion is not null);
        RuleFor(x => x.MachineName).MaximumLength(200).When(x => x.MachineName is not null);
        RuleFor(x => x.UserName).MaximumLength(200).When(x => x.UserName is not null);
        RuleFor(x => x.WorkspacePath).MaximumLength(1024).When(x => x.WorkspacePath is not null);
        RuleFor(x => x.RepositoryPath).MaximumLength(1024).When(x => x.RepositoryPath is not null);
        RuleFor(x => x.RemoteUrl).MaximumLength(1024).When(x => x.RemoteUrl is not null);
        RuleFor(x => x.Branch).MaximumLength(200).When(x => x.Branch is not null);
        RuleFor(x => x.ExternalSessionId).MaximumLength(200).When(x => x.ExternalSessionId is not null);

        RuleFor(x => x.EndedAtUtc)
            .GreaterThanOrEqualTo(x => x.StartedAtUtc)
            .When(x => x.EndedAtUtc.HasValue)
            .WithMessage("endedAtUtc cannot be earlier than startedAtUtc.");
    }
}

/// <summary>
/// Validates Cursor usage import requests.
/// </summary>
public sealed class ImportCursorUsageRequestDtoValidator : AbstractValidator<ImportCursorUsageRequestDto>
{
    public ImportCursorUsageRequestDtoValidator()
    {
        RuleFor(x => x.FilePath)
            .NotEmpty()
            .MaximumLength(1024);

        RuleFor(x => x.Format)
            .MaximumLength(32)
            .When(x => x.Format is not null);

        RuleFor(x => x.Timezone)
            .MaximumLength(64)
            .When(x => x.Timezone is not null);
    }
}

/// <summary>
/// Validates manual allocation requests.
/// </summary>
public sealed class AllocationRequestDtoValidator : AbstractValidator<AllocationRequestDto>
{
    public AllocationRequestDtoValidator()
    {
        RuleFor(x => x.UsageRecordId)
            .NotEmpty();

        RuleFor(x => x.ProjectAllocations)
            .NotNull()
            .Must(a => a.Count > 0)
            .WithMessage("At least one project allocation is required.");

        RuleForEach(x => x.ProjectAllocations).ChildRules(share =>
        {
            share.RuleFor(s => s.ProjectId).NotEmpty();
            share.RuleFor(s => s.Percentage).InclusiveBetween(0m, 100m);
        });

        RuleFor(x => x.ProjectAllocations)
            .Must(shares =>
            {
                var sum = shares.Sum(s => s.Percentage);
                return sum > 0m && sum <= 100.01m;
            })
            .WithMessage("Allocation percentages must sum to a positive value up to 100.");
    }
}

/// <summary>
/// Validates export requests.
/// </summary>
public sealed class ExportRequestDtoValidator : AbstractValidator<ExportRequestDto>
{
    public ExportRequestDtoValidator()
    {
        RuleFor(x => x.ReportType)
            .NotEmpty()
            .MaximumLength(64);

        RuleFor(x => x.ToUtc)
            .GreaterThan(x => x.FromUtc)
            .WithMessage("toUtc must be after fromUtc.");

        RuleFor(x => x.FileName)
            .MaximumLength(200)
            .Must(name => name is null || !name.Contains("..", StringComparison.Ordinal))
            .WithMessage("File name must not contain path traversal segments.");
    }
}
