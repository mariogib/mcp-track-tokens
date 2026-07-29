using FluentValidation;
using Microsoft.Extensions.Options;
using McpTrackTokens.Application.DTOs;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Application.Options;
using McpTrackTokens.Application.Services;
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
        api.MapPost("/usage/allocate", AllocateUsageAsync);
        api.MapPost("/usage/{id:guid}/allocate-to-prompt", AllocateUsageToClosestPromptAsync);

        api.MapGet("/projects", ListProjectsAsync);
        api.MapPost("/projects", CreateProjectAsync);
        api.MapGet("/projects/{id:guid}", GetProjectAsync);
        api.MapPut("/projects/{id:guid}", UpdateProjectAsync);
        api.MapDelete("/projects/{id:guid}", DeleteProjectAsync);
        api.MapGet("/projects/{id:guid}/activity", GetProjectActivityAsync);
        api.MapGet("/projects/{id:guid}/usage", GetProjectUsageAsync);
        api.MapGet("/projects/{id:guid}/cost", GetProjectCostAsync);
        api.MapGet("/projects/{id:guid}/token-cost", GetProjectTokenCostAsync);
        api.MapGet("/projects/{id:guid}/sessions", GetProjectSessionsAsync);
        api.MapPost("/projects/{id:guid}/sessions", CreateProjectSessionAsync);
        api.MapGet("/projects/{id:guid}/timesheet-entries", GetProjectTimesheetEntriesAsync);
        api.MapPost("/projects/{id:guid}/timesheet-entries", CreateProjectTimesheetEntryAsync);
        api.MapGet("/projects/{id:guid}/prompts", GetProjectPromptsAsync);
        api.MapGet("/projects/{id:guid}/prompts/facets", GetProjectPromptFacetsAsync);

        api.MapGet("/sessions/active", GetActiveSessionsAsync);
        api.MapGet("/sessions", GetSessionsAsync);
        api.MapGet("/sessions/{id:guid}/prompts", GetSessionPromptsAsync);
        api.MapPut("/sessions/{id:guid}", UpdateSessionAsync);
        api.MapDelete("/sessions/{id:guid}", DeleteSessionAsync);
        api.MapGet("/timesheet-entries", GetTimesheetEntriesAsync);
        api.MapGet("/timesheet/reports/overall", GetTimesheetOverallReportAsync);
        api.MapGet("/timesheet/reports/projects/{id:guid}", GetTimesheetProjectReportAsync);
        api.MapGet("/timesheet/reports/clients/{clientName}", GetTimesheetClientReportAsync);
        api.MapGet("/timesheet/reports/months", GetTimesheetReportMonthsAsync);
        api.MapPost("/timesheet/start", StartTimesheetAsync);
        api.MapPost("/timesheet/end", EndTimesheetAsync);
        api.MapPut("/timesheet-entries/{id:guid}", UpdateTimesheetEntryAsync);
        api.MapDelete("/timesheet-entries/{id:guid}", DeleteTimesheetEntryAsync);
        api.MapGet("/unallocated", GetUnallocatedAsync);
        api.MapGet("/unallocated/activity", GetUnallocatedActivityAsync);
        api.MapGet("/unallocated/usage", GetUnallocatedUsageAsync);
        api.MapDelete("/unallocated/usage", DeleteUnallocatedUsageAsync);
        api.MapGet("/usage/imported", GetImportedUsageAsync);
        api.MapPost("/activity/assign", AssignActivityAsync);
        api.MapPost("/activity/delete", DeleteActivityAsync);
        api.MapPost("/activity/windows/recalculate", RecalculateActivityWindowsAsync);
        api.MapGet("/reports/summary", GetSummaryAsync);
        api.MapGet("/reports/clients", ListReportClientsAsync);
        api.MapGet("/reports/clients/{clientName}/cost", GetClientCostAsync);
        api.MapGet("/reports/clients/{clientName}/token-cost", GetClientTokenCostAsync);
        api.MapGet("/reports/model-cost", GetModelCostAsync);
        api.MapGet("/reports/editors", GetEditorComparisonAsync);
        api.MapPost("/exports", ExportReportAsync);

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

        var preview = ReadFormOrQueryBool(httpRequest, form, "preview");
        var dryRun = ReadFormOrQueryBool(httpRequest, form, "dryRun");
        var force = ReadFormOrQueryBool(httpRequest, form, "force");
        var format = FirstNonEmpty(form["format"].ToString(), httpRequest.Query["format"].ToString());
        var timezone = FirstNonEmpty(form["timezone"].ToString(), httpRequest.Query["timezone"].ToString());
        var columnMappings = ParseColumnMappings(form["columnMappings"].ToString());

        var tempDirectory = Path.Combine(Path.GetTempPath(), "mcp-track-tokens-uploads");
        Directory.CreateDirectory(tempDirectory);
        var tempPath = Path.Combine(tempDirectory, $"{Guid.NewGuid():N}-{Path.GetFileName(file.FileName)}");

        try
        {
            await using (var stream = File.Create(tempPath))
            {
                await file.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
            }

            var request = new ImportCursorUsageRequestDto
            {
                FilePath = tempPath,
                Format = string.IsNullOrWhiteSpace(format) ? null : format,
                Timezone = string.IsNullOrWhiteSpace(timezone) ? null : timezone,
                DryRun = dryRun,
                Force = force,
                ColumnMappings = columnMappings
            };

            if (preview)
            {
                var previewResult = await importer.PreviewAsync(request, cancellationToken).ConfigureAwait(false);
                // Dashboard expects source column → canonical field.
                return Results.Ok(previewResult with
                {
                    ColumnMappings = InvertColumnMappings(previewResult.ColumnMappings)
                });
            }

            var result = await importer.ImportAsync(request, cancellationToken).ConfigureAwait(false);
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

    private static bool ReadFormOrQueryBool(HttpRequest request, IFormCollection form, string key)
    {
        if (bool.TryParse(form[key], out var fromForm))
        {
            return fromForm;
        }

        return bool.TryParse(request.Query[key], out var fromQuery) && fromQuery;
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static IReadOnlyDictionary<string, string>? ParseColumnMappings(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(raw);
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyDictionary<string, string> InvertColumnMappings(
        IReadOnlyDictionary<string, string> mappings)
    {
        var inverted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (canonical, source) in mappings)
        {
            if (string.IsNullOrWhiteSpace(canonical) ||
                string.IsNullOrWhiteSpace(source) ||
                string.Equals(source, "ignore", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            inverted[source] = canonical;
        }

        return inverted;
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

    private static async Task<IResult> AllocateUsageAsync(
        AllocationRequestDto request,
        IAttributionEngine attribution,
        IExternalUsageRepository usage,
        IProjectRepository projects,
        CancellationToken cancellationToken)
    {
        try
        {
            var rows = await attribution.AttributeManualAsync(request, cancellationToken).ConfigureAwait(false);
            var record = await usage.GetByIdAsync(request.UsageRecordId, cancellationToken).ConfigureAwait(false);
            return Results.Ok(await MapAttributionRowsAsync(rows, record, projects, cancellationToken).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    private static async Task<IResult> AllocateUsageToClosestPromptAsync(
        Guid id,
        IAttributionEngine attribution,
        IExternalUsageRepository usage,
        IProjectRepository projects,
        CancellationToken cancellationToken)
    {
        try
        {
            var record = await usage.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
            if (record is null)
            {
                return Results.NotFound();
            }

            var rows = await attribution.AttributeAsync(record, cancellationToken).ConfigureAwait(false);
            return Results.Ok(await MapAttributionRowsAsync(rows, record, projects, cancellationToken).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    private static async Task<List<UsageAttributionRow>> MapAttributionRowsAsync(
        IReadOnlyList<UsageAttribution> rows,
        ExternalUsageRecord? record,
        IProjectRepository projects,
        CancellationToken cancellationToken)
    {
        var projectNames = new Dictionary<Guid, string>();
        foreach (var projectId in rows.Select(r => r.ProjectId).Where(id => id is not null).Select(id => id!.Value).Distinct())
        {
            var project = await projects.GetByIdAsync(projectId, cancellationToken).ConfigureAwait(false);
            if (project is not null)
            {
                projectNames[projectId] = project.Name;
            }
        }

        return rows.Select(row => new UsageAttributionRow
        {
            UsageRecordId = row.ExternalUsageRecordId,
            AttributionId = row.Id,
            ProjectId = row.ProjectId,
            ProjectName = row.ProjectId is Guid pid && projectNames.TryGetValue(pid, out var name) ? name : null,
            ActivityEventId = row.ActivityEventId,
            TimestampUtc = record?.TimestampUtc ?? row.CreatedAtUtc,
            Model = record?.Model,
            Provider = record?.Provider?.ToString(),
            AllocatedCost = row.AllocatedCost,
            AllocationPercentage = row.AllocationPercentage,
            AllocatedTotalTokens = row.AllocatedTotalTokens,
            AttributionMethod = row.AttributionMethod.ToString(),
            Confidence = row.Confidence.ToString(),
            Reason = row.Reason
        }).ToList();
    }

    private static async Task<IResult> ListProjectsAsync(
        IProjectRepository projects,
        bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var list = await projects.ListAsync(activeOnly, cancellationToken).ConfigureAwait(false);
        return Results.Ok(list.Select(ProjectMapper.ToDto).ToList());
    }

    private static async Task<IResult> CreateProjectAsync(
        CreateProjectRequest request,
        IProjectDetectionService projects,
        CancellationToken cancellationToken)
    {
        try
        {
            var created = await projects.RegisterAsync(request, cancellationToken).ConfigureAwait(false);
            return Results.Created($"/api/v1/projects/{created.Id}", created);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
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
        var usage = await reports.GetProjectUsageSummaryAsync(id, from, to, cancellationToken).ConfigureAwait(false);
        var cost = await reports.GetProjectCostAsync(id, from, to, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(ProjectMapper.ToDetailDto(project, repositories, aliases, activity, usage, new CostSummaryDto
        {
            UsageBasedCost = cost.UsageBasedCursorCost,
            SubscriptionAllocation = cost.SubscriptionAllocation,
            OtherProviderCost = cost.OtherProviderCost,
            UnallocatedCost = cost.UnallocatedCost,
            TotalAiCost = cost.TotalAiCost,
            CalculatedTokenCost = cost.CalculatedTokenCost,
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
            var usage = await reports.GetProjectUsageSummaryAsync(id, from, to, cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(usage);
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

    private static async Task<IResult> GetProjectTokenCostAsync(
        Guid id,
        IReportService reports,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (from, to) = DateRange.Resolve(fromUtc, toUtc);
            var report = await reports
                .GetProjectTokenCostEstimateAsync(id, from, to, cancellationToken)
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
        int? pageIndex,
        int? pageSize,
        string? search,
        string? status,
        CancellationToken cancellationToken)
    {
        var project = await projects.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (project is null)
        {
            return Results.NotFound();
        }

        var (from, to) = DateRange.Resolve(fromUtc, toUtc);

        if (pageIndex is not null || pageSize is not null)
        {
            var index = Math.Max(0, pageIndex ?? 0);
            var size = Math.Clamp(pageSize ?? 25, 1, 100);
            var filter = new SessionPageFilter
            {
                ProjectId = id,
                FromUtc = from,
                ToUtc = to,
                Search = search,
                Status = status
            };
            var totalCount = await sessions.CountAsync(filter, cancellationToken).ConfigureAwait(false);
            var page = await sessions
                .ListPagedAsync(filter, index, size, cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(new PagedResultDto<object>
            {
                Items = page.Select(SessionMapper.ToDto).Cast<object>().ToList(),
                PageIndex = index,
                PageSize = size,
                TotalCount = totalCount
            });
        }

        var list = await sessions.ListByProjectAsync(id, from, to, cancellationToken).ConfigureAwait(false);
        return Results.Ok(list.Select(SessionMapper.ToDto).ToList());
    }

    private static async Task<IResult> GetSessionsAsync(
        ISessionRepository sessions,
        Guid? projectId,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        int? pageIndex,
        int? pageSize,
        string? search,
        string? status,
        CancellationToken cancellationToken)
    {
        var (from, to) = DateRange.Resolve(fromUtc, toUtc);

        if (pageIndex is not null || pageSize is not null)
        {
            var index = Math.Max(0, pageIndex ?? 0);
            var size = Math.Clamp(pageSize ?? 25, 1, 100);
            var filter = new SessionPageFilter
            {
                ProjectId = projectId,
                FromUtc = from,
                ToUtc = to,
                Search = search,
                Status = status
            };
            var totalCount = await sessions.CountAsync(filter, cancellationToken).ConfigureAwait(false);
            var page = await sessions
                .ListPagedAsync(filter, index, size, cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(new PagedResultDto<object>
            {
                Items = page.Select(SessionMapper.ToDto).Cast<object>().ToList(),
                PageIndex = index,
                PageSize = size,
                TotalCount = totalCount
            });
        }

        var list = await sessions.ListAsync(projectId, from, to, cancellationToken).ConfigureAwait(false);
        return Results.Ok(list.Select(SessionMapper.ToDto).ToList());
    }

    private static async Task<IResult> CreateProjectSessionAsync(
        Guid id,
        CreateProjectSessionRequest request,
        ISessionManagementService sessions,
        CancellationToken cancellationToken)
    {
        try
        {
            var session = await sessions.CreateForProjectAsync(id, request, cancellationToken).ConfigureAwait(false);
            return Results.Created($"/api/v1/sessions/{session.Id}", SessionMapper.ToDto(session));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    private static async Task<IResult> UpdateSessionAsync(
        Guid id,
        UpdateSessionRequest request,
        ISessionManagementService sessions,
        CancellationToken cancellationToken)
    {
        try
        {
            var session = await sessions.UpdateAsync(id, request, cancellationToken).ConfigureAwait(false);
            return Results.Ok(SessionMapper.ToDto(session));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    private static async Task<IResult> DeleteSessionAsync(
        Guid id,
        ISessionManagementService sessions,
        CancellationToken cancellationToken)
    {
        try
        {
            await sessions.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
            return Results.NoContent();
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    private static async Task<IResult> GetSessionPromptsAsync(
        Guid id,
        ISessionRepository sessions,
        IActivityEventRepository events,
        CancellationToken cancellationToken)
    {
        var session = await sessions.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            return Results.NotFound();
        }

        var prompts = await events.ListBySessionAsync(id, cancellationToken).ConfigureAwait(false);
        return Results.Ok(prompts
            .Where(e => e.EventType == ActivityEventType.PromptSubmitted)
            .OrderByDescending(e => e.TimestampUtc)
            .Select(ToPromptDto)
            .ToList());
    }

    private static async Task<IResult> GetProjectTimesheetEntriesAsync(
        Guid id,
        ITimesheetManagementService timesheets,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        int? pageIndex,
        int? pageSize,
        string? search,
        string? openClosed,
        CancellationToken cancellationToken)
    {
        try
        {
            if (pageIndex is not null || pageSize is not null)
            {
                var index = pageIndex ?? 0;
                var size = Math.Clamp(pageSize ?? 25, 1, 100);
                var paged = await timesheets.ListPagedAsync(
                        new TimesheetEntryPageFilter
                        {
                            ProjectId = id,
                            FromUtc = fromUtc,
                            ToUtc = toUtc,
                            Search = search,
                            OpenClosed = openClosed
                        },
                        index,
                        size,
                        cancellationToken)
                    .ConfigureAwait(false);
                return Results.Ok(paged);
            }

            var list = await timesheets.ListForProjectAsync(id, fromUtc, toUtc, cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(list);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    private static async Task<IResult> GetTimesheetEntriesAsync(
        ITimesheetManagementService timesheets,
        Guid? projectId,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        int? pageIndex,
        int? pageSize,
        string? search,
        string? openClosed,
        CancellationToken cancellationToken)
    {
        try
        {
            if (pageIndex is not null || pageSize is not null)
            {
                var index = pageIndex ?? 0;
                var size = Math.Clamp(pageSize ?? 25, 1, 100);
                var paged = await timesheets.ListPagedAsync(
                        new TimesheetEntryPageFilter
                        {
                            ProjectId = projectId,
                            FromUtc = fromUtc,
                            ToUtc = toUtc,
                            Search = search,
                            OpenClosed = openClosed
                        },
                        index,
                        size,
                        cancellationToken)
                    .ConfigureAwait(false);
                return Results.Ok(paged);
            }

            var list = await timesheets.ListAsync(projectId, fromUtc, toUtc, cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(list);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    private static async Task<IResult> GetTimesheetOverallReportAsync(
        ITimesheetReportService reports,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        int? timeZoneOffsetMinutes,
        CancellationToken cancellationToken)
    {
        try
        {
            var (from, to) = DateRange.Resolve(fromUtc, toUtc);
            var report = await reports
                .GetOverallReportAsync(from, to, timeZoneOffsetMinutes, cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(report);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    private static async Task<IResult> GetTimesheetReportMonthsAsync(
        ITimesheetReportService reports,
        Guid? projectId,
        string? clientName,
        CancellationToken cancellationToken)
    {
        try
        {
            var months = await reports
                .ListMonthsWithEntriesAsync(projectId, clientName, cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(months);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    private static async Task<IResult> GetTimesheetProjectReportAsync(
        Guid id,
        ITimesheetReportService reports,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        int? timeZoneOffsetMinutes,
        CancellationToken cancellationToken)
    {
        try
        {
            var (from, to) = DateRange.Resolve(fromUtc, toUtc);
            var report = await reports
                .GetProjectReportAsync(id, from, to, timeZoneOffsetMinutes, cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(report);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    private static async Task<IResult> GetTimesheetClientReportAsync(
        string clientName,
        ITimesheetReportService reports,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        int? timeZoneOffsetMinutes,
        CancellationToken cancellationToken)
    {
        try
        {
            var decoded = Uri.UnescapeDataString(clientName);
            var (from, to) = DateRange.Resolve(fromUtc, toUtc);
            var report = await reports
                .GetClientReportAsync(decoded, from, to, timeZoneOffsetMinutes, cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(report);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    private static async Task<IResult> CreateProjectTimesheetEntryAsync(
        Guid id,
        CreateTimesheetEntryRequest request,
        ITimesheetManagementService timesheets,
        CancellationToken cancellationToken)
    {
        try
        {
            var entry = await timesheets.CreateForProjectAsync(id, request, cancellationToken)
                .ConfigureAwait(false);
            return Results.Created($"/api/v1/timesheet-entries/{entry.Id}", entry);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    private static async Task<IResult> StartTimesheetAsync(
        StartTimesheetRequest request,
        ITimesheetManagementService timesheets,
        CancellationToken cancellationToken)
    {
        try
        {
            var entry = await timesheets.StartAsync(request, cancellationToken).ConfigureAwait(false);
            return Results.Ok(entry);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    private static async Task<IResult> EndTimesheetAsync(
        EndTimesheetRequest request,
        ITimesheetManagementService timesheets,
        CancellationToken cancellationToken)
    {
        try
        {
            var entry = await timesheets.EndAsync(request, cancellationToken).ConfigureAwait(false);
            return Results.Ok(entry);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    private static async Task<IResult> UpdateTimesheetEntryAsync(
        Guid id,
        UpdateTimesheetEntryRequest request,
        ITimesheetManagementService timesheets,
        CancellationToken cancellationToken)
    {
        try
        {
            var entry = await timesheets.UpdateAsync(id, request, cancellationToken).ConfigureAwait(false);
            return Results.Ok(entry);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    private static async Task<IResult> DeleteTimesheetEntryAsync(
        Guid id,
        ITimesheetManagementService timesheets,
        CancellationToken cancellationToken)
    {
        try
        {
            await timesheets.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
            return Results.NoContent();
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    private static async Task<IResult> GetProjectPromptsAsync(
        Guid id,
        IProjectRepository projects,
        IActivityEventRepository events,
        IUsageAttributionRepository attributions,
        IExternalUsageRepository usage,
        IOptions<TrackingOptions> trackingOptions,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        int? pageIndex,
        int? pageSize,
        string? search,
        string? status,
        string? eventType,
        string? model,
        string? branch,
        CancellationToken cancellationToken)
    {
        var project = await projects.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (project is null)
        {
            return Results.NotFound();
        }

        var (from, to) = DateRange.Resolve(fromUtc, toUtc);

        if (pageIndex is not null || pageSize is not null)
        {
            var index = Math.Max(0, pageIndex ?? 0);
            var size = Math.Clamp(pageSize ?? 25, 1, 100);
            var filter = new ActivityEventPageFilter
            {
                ProjectId = id,
                FromUtc = from,
                ToUtc = to,
                Search = search,
                Status = status,
                EventType = eventType,
                Model = model,
                Branch = branch,
                PromptSubmittedOnly = true
            };
            var totalCount = await events.CountAsync(filter, cancellationToken).ConfigureAwait(false);
            var prompts = await events
                .ListPagedAsync(filter, index, size, cancellationToken)
                .ConfigureAwait(false);
            var items = await MapPromptsWithUsageAsync(
                    prompts,
                    attributions,
                    usage,
                    trackingOptions,
                    cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(new PagedResultDto<object>
            {
                Items = items,
                PageIndex = index,
                PageSize = size,
                TotalCount = totalCount
            });
        }

        var list = await events.ListAsync(from, to, id, unallocatedOnly: null, cancellationToken)
            .ConfigureAwait(false);
        var allPrompts = list
            .Where(e => e.EventType == ActivityEventType.PromptSubmitted)
            .OrderByDescending(e => e.TimestampUtc)
            .ToList();
        var mapped = await MapPromptsWithUsageAsync(
                allPrompts,
                attributions,
                usage,
                trackingOptions,
                cancellationToken)
            .ConfigureAwait(false);
        return Results.Ok(mapped);
    }

    private static async Task<IResult> GetProjectPromptFacetsAsync(
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
        var facets = await events
            .GetPromptFacetsAsync(id, from, to, cancellationToken)
            .ConfigureAwait(false);
        return Results.Ok(facets);
    }

    private static async Task<IReadOnlyList<object>> MapPromptsWithUsageAsync(
        IReadOnlyList<PromptActivityEvent> prompts,
        IUsageAttributionRepository attributions,
        IExternalUsageRepository usage,
        IOptions<TrackingOptions> trackingOptions,
        CancellationToken cancellationToken)
    {
        if (prompts.Count == 0)
        {
            return [];
        }

        var linked = await attributions
            .ListByActivityEventIdsAsync(prompts.Select(p => p.Id).ToList(), cancellationToken)
            .ConfigureAwait(false);
        var usageIds = linked.Select(a => a.ExternalUsageRecordId).Distinct().ToList();
        var usageById = new Dictionary<Guid, ExternalUsageRecord>();
        foreach (var usageId in usageIds)
        {
            var record = await usage.GetByIdAsync(usageId, cancellationToken).ConfigureAwait(false);
            if (record is not null)
            {
                usageById[record.Id] = record;
            }
        }

        var rates = trackingOptions.Value.CursorTokenRates.Count > 0
            ? trackingOptions.Value.CursorTokenRates
            : CursorTokenCostCalculator.CreateDefaultRates();

        var usageByPrompt = linked
            .Where(a => a.ActivityEventId is Guid)
            .GroupBy(a => a.ActivityEventId!.Value)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    long tokens = 0;
                    decimal cost = 0m;
                    decimal calculated = 0m;
                    foreach (var attr in g)
                    {
                        if (attr.AllocatedTotalTokens > 0 || attr.AllocatedCost > 0)
                        {
                            tokens += attr.AllocatedTotalTokens;
                            cost += attr.AllocatedCost;
                        }
                        else if (usageById.TryGetValue(attr.ExternalUsageRecordId, out var fallback))
                        {
                            tokens += fallback.TotalTokens
                                ?? ((fallback.InputTokens ?? 0) + (fallback.OutputTokens ?? 0)
                                    + (fallback.CachedInputTokens ?? 0) + (fallback.ReasoningTokens ?? 0));
                            cost += fallback.ReportedCost ?? 0m;
                        }

                        if (usageById.TryGetValue(attr.ExternalUsageRecordId, out var record) &&
                            CursorTokenCostCalculator.ResolveRate(rates, record.Model) is { } rate)
                        {
                            calculated += CursorTokenCostCalculator.Estimate(
                                record,
                                attr.AllocationPercentage > 0m ? attr.AllocationPercentage : 100m,
                                rate);
                        }
                    }

                    return (
                        Tokens: tokens,
                        Cost: cost,
                        CalculatedTokenCost: Math.Round(calculated, 6, MidpointRounding.AwayFromZero),
                        Count: g.Count(),
                        Linked: true);
                });

        return prompts.Select(e =>
        {
            usageByPrompt.TryGetValue(e.Id, out var linkedUsage);
            return (object)new
            {
                e.Id,
                e.TimestampUtc,
                eventType = e.EventType.ToString(),
                editor = e.Editor.ToString(),
                e.Model,
                e.Branch,
                status = e.Status.ToString(),
                e.DurationMilliseconds,
                e.RepositoryPath,
                totalTokens = linkedUsage.Linked ? linkedUsage.Tokens : (long?)null,
                reportedCost = linkedUsage.Linked ? linkedUsage.Cost : (decimal?)null,
                calculatedTokenCost = linkedUsage.Linked ? linkedUsage.CalculatedTokenCost : (decimal?)null,
                linkedUsageCount = linkedUsage.Linked ? linkedUsage.Count : 0,
                hasLinkedUsage = linkedUsage.Linked
            };
        }).ToList();
    }

    private static object ToPromptDto(PromptActivityEvent e) => new
    {
        e.Id,
        e.TimestampUtc,
        eventType = e.EventType.ToString(),
        editor = e.Editor.ToString(),
        e.Model,
        e.Branch,
        status = e.Status.ToString(),
        e.DurationMilliseconds,
        e.RepositoryPath,
        totalTokens = (long?)null,
        reportedCost = (decimal?)null,
        linkedUsageCount = 0,
        hasLinkedUsage = false
    };

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

    private static async Task<IResult> GetUnallocatedActivityAsync(
        IReportService reports,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        int? limit,
        CancellationToken cancellationToken)
    {
        var (from, to) = DateRange.Resolve(fromUtc, toUtc);
        var activity = await reports.GetUnallocatedActivityAsync(from, to, limit, cancellationToken)
            .ConfigureAwait(false);
        return Results.Ok(activity);
    }

    private static async Task<IResult> GetUnallocatedUsageAsync(
        IReportService reports,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        int? limit,
        CancellationToken cancellationToken)
    {
        var (from, to) = DateRange.Resolve(fromUtc, toUtc);
        var usage = await reports.GetUnallocatedUsageAsync(from, to, limit, cancellationToken)
            .ConfigureAwait(false);
        return Results.Ok(usage);
    }

    private static async Task<IResult> DeleteUnallocatedUsageAsync(
        IExternalUsageRepository usage,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken cancellationToken)
    {
        var (from, to) = DateRange.Resolve(fromUtc, toUtc);
        try
        {
            var deletedCount = await usage.DeleteUnallocatedAsync(from, to, cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(new DeleteUnallocatedUsageResultDto
            {
                FromUtc = from,
                ToUtc = to,
                DeletedCount = deletedCount
            });
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    private static async Task<IResult> GetImportedUsageAsync(
        IReportService reports,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        int? limit,
        CancellationToken cancellationToken)
    {
        var (from, to) = DateRange.Resolve(fromUtc, toUtc);
        var usage = await reports.GetImportedUsageAsync(from, to, limit, cancellationToken)
            .ConfigureAwait(false);
        return Results.Ok(usage);
    }

    private static async Task<IResult> AssignActivityAsync(
        AssignActivityRequestDto request,
        IProjectRepository projects,
        IActivityEventRepository events,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        if (request.EventIds is null || request.EventIds.Count == 0)
        {
            return Results.BadRequest(new { error = "At least one event id is required." });
        }

        var project = await projects.GetByIdAsync(request.ProjectId, cancellationToken).ConfigureAwait(false);
        if (project is null || !project.IsActive)
        {
            return Results.NotFound(new { error = "Project not found or inactive." });
        }

        await events.AssignProjectAsync(
                request.EventIds,
                request.ProjectId,
                AttributionMethod.Manual,
                AttributionConfidence.High,
                cancellationToken)
            .ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Results.Ok(new AssignActivityResultDto
        {
            ProjectId = request.ProjectId,
            Assigned = request.EventIds.Count
        });
    }

    private static async Task<IResult> DeleteActivityAsync(
        DeleteActivityRequestDto request,
        IActivityEventRepository events,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        if (request.EventIds is null || request.EventIds.Count == 0)
        {
            return Results.BadRequest(new { error = "At least one event id is required." });
        }

        try
        {
            var deleted = await events
                .DeleteUnallocatedByIdsAsync(request.EventIds, cancellationToken)
                .ConfigureAwait(false);
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Results.Ok(new DeleteActivityResultDto { Deleted = deleted });
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    private static async Task<IResult> RecalculateActivityWindowsAsync(
        RecalculateWindowsRequestDto request,
        IActivityWindowService windows,
        CancellationToken cancellationToken)
    {
        var from = request.FromUtc.ToUniversalTime();
        var to = request.ToUtc.ToUniversalTime();
        if (from > to)
        {
            (from, to) = (to, from);
        }

        try
        {
            var result = await windows
                .RecalculateAsync(
                    request.ProjectId,
                    from,
                    to,
                    request.InactivityThresholdMinutes,
                    request.DryRun,
                    cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
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

    private static async Task<IResult> ListReportClientsAsync(
        IProjectRepository projects,
        CancellationToken cancellationToken)
    {
        var list = await projects.ListAsync(activeOnly: true, cancellationToken).ConfigureAwait(false);
        var clients = list
            .Where(p => !string.IsNullOrWhiteSpace(p.ClientName))
            .GroupBy(p => p.ClientName!.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new
            {
                name = g.First().ClientName!.Trim(),
                projectCount = g.Count(),
                currency = g.Select(p => p.Currency).FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)) ?? "USD"
            })
            .ToList();
        return Results.Ok(clients);
    }

    private static async Task<IResult> GetClientCostAsync(
        string clientName,
        IReportService reports,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            var decoded = Uri.UnescapeDataString(clientName);
            var (from, to) = DateRange.Resolve(fromUtc, toUtc);
            var report = await reports.GetClientCostAsync(decoded, from, to, cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(report);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    private static async Task<IResult> GetClientTokenCostAsync(
        string clientName,
        IReportService reports,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            var decoded = Uri.UnescapeDataString(clientName);
            var (from, to) = DateRange.Resolve(fromUtc, toUtc);
            var report = await reports
                .GetClientTokenCostEstimateAsync(decoded, from, to, cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(report);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    private static async Task<IResult> GetModelCostAsync(
        IReportService reports,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken cancellationToken)
    {
        var (from, to) = DateRange.Resolve(fromUtc, toUtc);
        var report = await reports.GetModelCostAsync(from, to, cancellationToken).ConfigureAwait(false);
        return Results.Ok(report);
    }

    private static async Task<IResult> GetEditorComparisonAsync(
        IReportService reports,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken cancellationToken)
    {
        var (from, to) = DateRange.Resolve(fromUtc, toUtc);
        var report = await reports.GetEditorComparisonAsync(from, to, cancellationToken).ConfigureAwait(false);
        return Results.Ok(report);
    }

    private static async Task<IResult> ExportReportAsync(
        ExportRequestDto request,
        IExportService export,
        CancellationToken cancellationToken)
    {
        try
        {
            var file = await export.BuildFileAsync(request, cancellationToken).ConfigureAwait(false);
            return Results.File(file.Content, file.ContentType, file.FileName);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
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

