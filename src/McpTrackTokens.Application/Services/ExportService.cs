using FluentValidation;
using Microsoft.Extensions.Options;
using McpTrackTokens.Application.DTOs;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Application.Options;
using McpTrackTokens.Domain.Enums;
using DomainValidationException = McpTrackTokens.Domain.Exceptions.ValidationException;

namespace McpTrackTokens.Application.Services;

/// <summary>
/// Exports reports to CSV/JSON/Markdown with path-traversal protection for disk writes.
/// </summary>
public sealed class ExportService : IExportService
{
    private readonly IReportService _reports;
    private readonly IReportExporter _exporter;
    private readonly IValidator<ExportRequestDto> _validator;
    private readonly TrackingOptions _options;

    public ExportService(
        IReportService reports,
        IReportExporter exporter,
        IValidator<ExportRequestDto> validator,
        IOptions<TrackingOptions> options)
    {
        _reports = reports;
        _exporter = exporter;
        _validator = validator;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<ExportFileDto> BuildFileAsync(
        ExportRequestDto request,
        CancellationToken cancellationToken = default)
    {
        await _validator.ValidateAndThrowAsync(request, cancellationToken).ConfigureAwait(false);

        var report = await BuildReportAsync(request, cancellationToken).ConfigureAwait(false);
        var fileName = string.IsNullOrWhiteSpace(request.FileName)
            ? BuildDefaultFileName(request)
            : SanitizeFileName(request.FileName!);
        var content = _exporter.Render(report, request.Format);
        var exportedAt = DateTimeOffset.UtcNow;

        return new ExportFileDto
        {
            FileName = fileName,
            ContentType = ContentTypeFor(request.Format),
            Content = content,
            Format = request.Format,
            ByteCount = content.LongLength,
            ExportedAtUtc = exportedAt
        };
    }

    /// <inheritdoc />
    public async Task<ExportResultDto> ExportAsync(
        ExportRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var file = await BuildFileAsync(request, cancellationToken).ConfigureAwait(false);
        var directory = ResolveOutputDirectory(request.OutputDirectory);
        Directory.CreateDirectory(directory);

        var fullPath = Path.GetFullPath(Path.Combine(directory, file.FileName));
        EnsureApprovedPath(fullPath);
        await File.WriteAllBytesAsync(fullPath, file.Content, cancellationToken).ConfigureAwait(false);

        return new ExportResultDto
        {
            FilePath = fullPath,
            Format = file.Format,
            ByteCount = file.ByteCount,
            ExportedAtUtc = file.ExportedAtUtc
        };
    }

    private async Task<object> BuildReportAsync(ExportRequestDto request, CancellationToken cancellationToken)
    {
        var type = request.ReportType.Trim().ToLowerInvariant();
        return type switch
        {
            "dailyactivity" or "daily-activity" or "daily" =>
                await _reports.GetDailyActivityAsync(request.FromUtc, request.ToUtc, request.ProjectId, cancellationToken)
                    .ConfigureAwait(false),
            "projectactivity" or "project-activity" or "activity" =>
                await _reports.GetProjectActivityAsync(
                        RequireProjectId(request),
                        request.FromUtc,
                        request.ToUtc,
                        cancellationToken)
                    .ConfigureAwait(false),
            "projectcost" or "project-cost" or "cost" =>
                await _reports.GetProjectCostAsync(
                        RequireProjectId(request),
                        request.FromUtc,
                        request.ToUtc,
                        includeSubscriptionAllocation: request.IncludeCosts,
                        cancellationToken)
                    .ConfigureAwait(false),
            "usageattribution" or "usage-attribution" or "attribution" =>
                await _reports.GetUsageAttributionAsync(request.FromUtc, request.ToUtc, request.ProjectId, cancellationToken)
                    .ConfigureAwait(false),
            "unallocatedusage" or "unallocated-usage" or "unallocated" =>
                await _reports.GetUnallocatedUsageAsync(request.FromUtc, request.ToUtc, cancellationToken: cancellationToken)
                    .ConfigureAwait(false),
            "monthlysummary" or "monthly-summary" or "monthly" =>
                await _reports.GetMonthlySummaryAsync(request.FromUtc.Year, request.FromUtc.Month, cancellationToken)
                    .ConfigureAwait(false),
            "editorcomparison" or "editor-comparison" or "editors" =>
                await _reports.GetEditorComparisonAsync(request.FromUtc, request.ToUtc, cancellationToken)
                    .ConfigureAwait(false),
            "modelcost" or "model-cost" or "models" =>
                await _reports.GetModelCostAsync(request.FromUtc, request.ToUtc, cancellationToken)
                    .ConfigureAwait(false),
            "project" or "project-report" or "full" =>
                await BuildProjectReportAsync(request, cancellationToken).ConfigureAwait(false),
            _ => throw new DomainValidationException(nameof(request.ReportType), $"Unknown report type '{request.ReportType}'.")
        };
    }

    private async Task<object> BuildProjectReportAsync(ExportRequestDto request, CancellationToken cancellationToken)
    {
        var projectId = RequireProjectId(request);
        var includeActivity = request.IncludeActivity;
        var includeUsage = request.IncludeUsage;
        var includeCosts = request.IncludeCosts;
        if (!includeActivity && !includeUsage && !includeCosts)
        {
            includeActivity = includeUsage = includeCosts = true;
        }

        object? activity = null;
        object? usage = null;
        object? cost = null;

        if (includeActivity)
        {
            activity = await _reports
                .GetProjectActivityAsync(projectId, request.FromUtc, request.ToUtc, cancellationToken)
                .ConfigureAwait(false);
        }

        if (includeUsage)
        {
            usage = await _reports
                .GetProjectUsageSummaryAsync(projectId, request.FromUtc, request.ToUtc, cancellationToken)
                .ConfigureAwait(false);
        }

        if (includeCosts)
        {
            cost = await _reports
                .GetProjectCostAsync(
                    projectId,
                    request.FromUtc,
                    request.ToUtc,
                    includeSubscriptionAllocation: true,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return new
        {
            ProjectId = projectId,
            FromUtc = request.FromUtc,
            ToUtc = request.ToUtc,
            Activity = activity,
            Usage = usage,
            Cost = cost
        };
    }

    private string ResolveOutputDirectory(string? outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            return _options.GetResolvedExportPath();
        }

        if (outputDirectory.Contains("..", StringComparison.Ordinal))
        {
            throw new DomainValidationException(nameof(outputDirectory), "Path traversal is not allowed.");
        }

        var resolved = TrackingOptions.ExpandPath(outputDirectory);
        var full = Path.GetFullPath(resolved);
        EnsureApprovedDirectory(full);
        return full;
    }

    private void EnsureApprovedDirectory(string directory)
    {
        var full = Path.GetFullPath(directory);
        if (!full.EndsWith(Path.DirectorySeparatorChar) && !full.EndsWith(Path.AltDirectorySeparatorChar))
        {
            full += Path.DirectorySeparatorChar;
        }

        var approved = _options.GetApprovedExportRoots();
        if (!approved.Any(root => full.StartsWith(root, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainValidationException(
                "outputDirectory",
                "Export directory must be under an approved export path.");
        }
    }

    private void EnsureApprovedPath(string filePath)
    {
        var full = Path.GetFullPath(filePath);
        var directory = Path.GetDirectoryName(full)
            ?? throw new DomainValidationException("filePath", "Invalid export file path.");
        EnsureApprovedDirectory(directory);

        if (full.Contains("..", StringComparison.Ordinal))
        {
            throw new DomainValidationException("filePath", "Path traversal is not allowed.");
        }
    }

    private static Guid RequireProjectId(ExportRequestDto request)
        => request.ProjectId ?? throw new DomainValidationException(nameof(request.ProjectId), "projectId is required for this report.");

    private static string BuildDefaultFileName(ExportRequestDto request)
    {
        var stamp = $"{request.FromUtc:yyyy-MM}";
        var slug = request.ProjectId?.ToString("N")[..8] ?? "all-projects";
        var extension = request.Format switch
        {
            ExportFormat.Csv or ExportFormat.ExcelCsv => "csv",
            ExportFormat.Markdown => "md",
            _ => "json"
        };
        return $"{slug}_{stamp}_{request.ReportType.ToLowerInvariant()}.{extension}";
    }

    private static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(name) || name.Contains("..", StringComparison.Ordinal))
        {
            throw new DomainValidationException(nameof(fileName), "Invalid export file name.");
        }

        return name;
    }

    private static string ContentTypeFor(ExportFormat format)
        => format switch
        {
            ExportFormat.Csv or ExportFormat.ExcelCsv => "text/csv; charset=utf-8",
            ExportFormat.Markdown => "text/markdown; charset=utf-8",
            _ => "application/json; charset=utf-8"
        };
}
