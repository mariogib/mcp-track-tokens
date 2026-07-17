using FluentValidation;
using McpTrackTokens.Application.DTOs;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Domain.Entities;
using McpTrackTokens.Domain.Enums;
using McpTrackTokens.Domain.Exceptions;
using McpTrackTokens.Server.Mapping;
using DomainValidationException = McpTrackTokens.Domain.Exceptions.ValidationException;

namespace McpTrackTokens.Server.Endpoints;

/// <summary>
/// Maps the versioned HTTP ingestion and reporting API.
/// </summary>
public static class ApiEndpoints
{
    public static IEndpointRouteBuilder MapApiEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1");

        api.MapPost("/events", IngestEventAsync);
        api.MapPost("/events/batch", IngestBatchAsync);
        api.MapPost("/sessions/start", StartSessionAsync);
        api.MapPost("/sessions/end", EndSessionAsync);
        api.MapPost("/sessions/heartbeat", HeartbeatAsync);

        api.MapPost("/imports/cursor", ImportCursorAsync);
        api.MapPost("/imports/cursor/upload", UploadCursorAsync).DisableAntiforgery();
        api.MapPost("/reconciliation/run", RunReconciliationAsync);

        api.MapGet("/projects", ListProjectsAsync);
        api.MapGet("/projects/{id:guid}", GetProjectAsync);
        api.MapPut("/projects/{id:guid}", UpdateProjectAsync);
        api.MapDelete("/projects/{id:guid}", DeleteProjectAsync);
        api.MapGet("/projects/{id:guid}/activity", GetProjectActivityAsync);
        api.MapGet("/projects/{id:guid}/usage", GetProjectUsageAsync);
        api.MapGet("/projects/{id:guid}/cost", GetProjectCostAsync);
        api.MapGet("/projects/{id:guid}/sessions", GetProjectSessionsAsync);
        api.MapGet("/projects/{id:guid}/prompts", GetProjectPromptsAsync);

        api.MapGet("/sessions/active", GetActiveSessionsAsync);
        api.MapGet("/unallocated", GetUnallocatedAsync);
        api.MapGet("/reports/summary", GetSummaryAsync);

        return app;
    }

    private static async Task<IResult> IngestEventAsync(
        IngestEventDto dto,
        IEventIngestionService ingestion,
        IValidator<IngestEventDto> validator,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(dto, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.ToDictionary());
        }

        try
        {
            var result = await ingestion.IngestAsync(dto, cancellationToken).ConfigureAwait(false);
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    private static async Task<IResult> IngestBatchAsync(
        BatchIngestRequestDto request,
        IEventIngestionService ingestion,
        IValidator<BatchIngestRequestDto> validator,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.ToDictionary());
        }

        try
        {
            var result = await ingestion.IngestBatchAsync(request, cancellationToken).ConfigureAwait(false);
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    private static async Task<IResult> StartSessionAsync(
        SessionStartDto dto,
        IEventIngestionService ingestion,
        CancellationToken cancellationToken)
    {
        try
        {
            var session = await ingestion.StartSessionAsync(dto, cancellationToken).ConfigureAwait(false);
            return Results.Ok(SessionMapper.ToDto(session));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    private static async Task<IResult> EndSessionAsync(
        SessionEndDto dto,
        IEventIngestionService ingestion,
        CancellationToken cancellationToken)
    {
        try
        {
            var session = await ingestion.EndSessionAsync(dto, cancellationToken).ConfigureAwait(false);
            return session is null ? Results.NotFound() : Results.Ok(SessionMapper.ToDto(session));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    private static async Task<IResult> HeartbeatAsync(
        HeartbeatDto dto,
        IEventIngestionService ingestion,
        CancellationToken cancellationToken)
    {
        try
        {
            var session = await ingestion.HeartbeatAsync(dto, cancellationToken).ConfigureAwait(false);
            return session is null ? Results.NotFound() : Results.Ok(SessionMapper.ToDto(session));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    private static async Task<IResult> ImportCursorAsync(
        ImportCursorUsageRequestDto request,
        ICursorUsageImporter importer,
        IValidator<ImportCursorUsageRequestDto> validator,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.ToDictionary());
        }

        try
        {
            var result = await importer.ImportAsync(request, cancellationToken).ConfigureAwait(false);
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    private static async Task<IResult> UploadCursorAsync(
        HttpRequest httpRequest,
        ICursorUsageImporter importer,
        CancellationToken cancellationToken)
    {
        if (!httpRequest.HasFormContentType)
        {
            return Results.BadRequest(new { error = "multipart/form-data with a file field is required." });
        }

        var form = await httpRequest.ReadFormAsync(cancellationToken).ConfigureAwait(false);
        var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
        if (file is null || file.Length == 0)
        {
            return Results.BadRequest(new { error = "A non-empty file upload is required." });
        }

        var dryRun = bool.TryParse(form["dryRun"], out var dry) && dry;
        var force = bool.TryParse(form["force"], out var forceValue) && forceValue;
        var format = form["format"].ToString();
        var timezone = form["timezone"].ToString();

        var tempDirectory = Path.Combine(Path.GetTempPath(), "mcp-track-tokens-uploads");
        Directory.CreateDirectory(tempDirectory);
        var tempPath = Path.Combine(tempDirectory, $"{Guid.NewGuid():N}-{Path.GetFileName(file.FileName)}");

        try
        {
            await using (var stream = File.Create(tempPath))
            {
                await file.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
            }

            var result = await importer.ImportAsync(
                new ImportCursorUsageRequestDto
                {
                    FilePath = tempPath,
                    Format = string.IsNullOrWhiteSpace(format) ? null : format,
                    Timezone = string.IsNullOrWhiteSpace(timezone) ? null : timezone,
                    DryRun = dryRun,
                    Force = force
                },
                cancellationToken).ConfigureAwait(false);

            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }

    private static async Task<IResult> RunReconciliationAsync(
        ReconciliationRequestDto request,
        IReconciliationService reconciliation,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await reconciliation.RunAsync(request, cancellationToken).ConfigureAwait(false);
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    private static async Task<IResult> ListProjectsAsync(
        IProjectRepository projects,
        bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var list = await projects.ListAsync(activeOnly, cancellationToken).ConfigureAwait(false);
        return Results.Ok(list.Select(ProjectMapper.ToDto).ToList());
    }

    private static async Task<IResult> UpdateProjectAsync(
        Guid id,
        UpdateProjectRequest request,
        IProjectDetectionService projects,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await projects.UpdateAsync(id, request, cancellationToken).ConfigureAwait(false);
            return Results.Ok(updated);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    private static async Task<IResult> DeleteProjectAsync(
        Guid id,
        IProjectDetectionService projects,
        CancellationToken cancellationToken)
    {
        try
        {
            await projects.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
            return Results.NoContent();
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    private static async Task<IResult> GetProjectAsync(
        Guid id,
        IProjectRepository projects,
        IReportService reports,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken cancellationToken)
    {
        var project = await projects.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (project is null)
        {
            return Results.NotFound();
        }

        var (from, to) = DateRange.Resolve(fromUtc, toUtc);
        var repositories = await projects.GetRepositoriesAsync(id, cancellationToken).ConfigureAwait(false);
        var aliases = await projects.GetAliasesAsync(id, cancellationToken).ConfigureAwait(false);
        var activity = await reports.GetActivitySummaryAsync(id, from, to, cancellationToken).ConfigureAwait(false);
        var cost = await reports.GetProjectCostAsync(id, from, to, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(ProjectMapper.ToDetailDto(project, repositories, aliases, activity, new UsageSummaryDto
        {
            TotalTokens = cost.ImportedTotalTokens,
            ReportedCost = cost.UsageBasedCursorCost,
            Currency = cost.Currency,
            FromUtc = from,
            ToUtc = to
        }, new CostSummaryDto
        {
            UsageBasedCost = cost.UsageBasedCursorCost,
            SubscriptionAllocation = cost.SubscriptionAllocation,
            OtherProviderCost = cost.OtherProviderCost,
            UnallocatedCost = cost.UnallocatedCost,
            TotalAiCost = cost.TotalAiCost,
            Currency = cost.Currency,
            FromUtc = from,
            ToUtc = to
        }));
    }

    private static async Task<IResult> GetProjectActivityAsync(
        Guid id,
        IReportService reports,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            var (from, to) = DateRange.Resolve(fromUtc, toUtc);
            var report = await reports.GetProjectActivityAsync(id, from, to, cancellationToken).ConfigureAwait(false);
            return Results.Ok(report);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    private static async Task<IResult> GetProjectUsageAsync(
        Guid id,
        IReportService reports,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            var (from, to) = DateRange.Resolve(fromUtc, toUtc);
            var cost = await reports.GetProjectCostAsync(id, from, to, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var attribution = await reports.GetUsageAttributionAsync(from, to, id, cancellationToken)
                .ConfigureAwait(false);

            return Results.Ok(new
            {
                projectId = id,
                fromUtc = from,
                toUtc = to,
                summary = new UsageSummaryDto
                {
                    TotalTokens = cost.ImportedTotalTokens,
                    ReportedCost = cost.UsageBasedCursorCost,
                    Currency = cost.Currency,
                    RequestCount = attribution.Rows.Count,
                    FromUtc = from,
                    ToUtc = to
                },
                attribution
            });
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    private static async Task<IResult> GetProjectCostAsync(
        Guid id,
        IReportService reports,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        bool includeSubscriptionAllocation = true,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (from, to) = DateRange.Resolve(fromUtc, toUtc);
            var report = await reports
                .GetProjectCostAsync(id, from, to, includeSubscriptionAllocation, cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(report);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    private static async Task<IResult> GetProjectSessionsAsync(
        Guid id,
        IProjectRepository projects,
        ISessionRepository sessions,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken cancellationToken)
    {
        var project = await projects.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (project is null)
        {
            return Results.NotFound();
        }

        var (from, to) = DateRange.Resolve(fromUtc, toUtc);
        var list = await sessions.ListByProjectAsync(id, from, to, cancellationToken).ConfigureAwait(false);
        return Results.Ok(list.Select(SessionMapper.ToDto).ToList());
    }

    private static async Task<IResult> GetProjectPromptsAsync(
        Guid id,
        IProjectRepository projects,
        IActivityEventRepository events,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken cancellationToken)
    {
        var project = await projects.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (project is null)
        {
            return Results.NotFound();
        }

        var (from, to) = DateRange.Resolve(fromUtc, toUtc);
        var list = await events.ListAsync(from, to, id, unallocatedOnly: null, cancellationToken)
            .ConfigureAwait(false);
        return Results.Ok(list.Select(e => new
        {
            e.Id,
            e.TimestampUtc,
            eventType = e.EventType.ToString(),
            editor = e.Editor.ToString(),
            e.Model,
            e.Branch,
            status = e.Status.ToString(),
            e.DurationMilliseconds,
            e.RepositoryPath
        }).ToList());
    }

    private static async Task<IResult> GetActiveSessionsAsync(
        ISessionRepository sessions,
        CancellationToken cancellationToken)
    {
        var active = await sessions.GetActiveAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(active.Select(SessionMapper.ToDto).ToList());
    }

    private static async Task<IResult> GetUnallocatedAsync(
        IReportService reports,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        int? limit,
        CancellationToken cancellationToken)
    {
        var (from, to) = DateRange.Resolve(fromUtc, toUtc);
        var activity = await reports.GetUnallocatedActivityAsync(from, to, limit, cancellationToken)
            .ConfigureAwait(false);
        var usage = await reports.GetUnallocatedUsageAsync(from, to, limit, cancellationToken)
            .ConfigureAwait(false);
        return Results.Ok(new { activity, usage });
    }

    private static async Task<IResult> GetSummaryAsync(
        IReportService reports,
        int? year,
        int? month,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken cancellationToken)
    {
        if (year is int y && month is int m)
        {
            var monthly = await reports.GetMonthlySummaryAsync(y, m, cancellationToken).ConfigureAwait(false);
            return Results.Ok(monthly);
        }

        var (from, to) = DateRange.Resolve(fromUtc, toUtc);
        var status = await reports.GetTrackingStatusAsync(cancellationToken).ConfigureAwait(false);
        var activity = await reports.GetActivitySummaryAsync(null, from, to, cancellationToken).ConfigureAwait(false);
        var unallocatedUsage = await reports.GetUnallocatedUsageAsync(from, to, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return Results.Ok(new
        {
            fromUtc = from,
            toUtc = to,
            status,
            activity,
            unallocatedUsage
        });
    }

    private static IResult MapException(Exception ex) => ex switch
    {
        FluentValidation.ValidationException fv => Results.ValidationProblem(
            fv.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray())),
        DomainValidationException dve => Results.BadRequest(new { error = dve.Message, property = dve.PropertyName }),
        EntityNotFoundException nf => Results.NotFound(new { error = nf.Message }),
        DuplicateEntityException dup => Results.Conflict(new { error = dup.Message }),
        AttributionException attr => Results.BadRequest(new { error = attr.Message }),
        DomainException domain => Results.BadRequest(new { error = domain.Message }),
        ArgumentException arg => Results.BadRequest(new { error = arg.Message }),
        FileNotFoundException fnf => Results.NotFound(new { error = fnf.Message }),
        _ => Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError)
    };
}

internal static class DateRange
{
    public static (DateTimeOffset From, DateTimeOffset To) Resolve(DateTimeOffset? fromUtc, DateTimeOffset? toUtc)
    {
        var to = toUtc?.ToUniversalTime() ?? DateTimeOffset.UtcNow;
        var from = fromUtc?.ToUniversalTime() ?? to.AddDays(-30);
        if (from > to)
        {
            (from, to) = (to, from);
        }

        return (from, to);
    }
}
