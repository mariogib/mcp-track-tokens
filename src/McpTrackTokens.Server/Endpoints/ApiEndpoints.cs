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
        api.MapPost("/usage/allocate", AllocateUsageAsync);
        api.MapPost("/usage/{id:guid}/allocate-to-prompt", AllocateUsageToClosestPromptAsync);

        api.MapGet("/projects", ListProjectsAsync);
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

        api.MapGet("/sessions/active", GetActiveSessionsAsync);
        api.MapPut("/sessions/{id:guid}", UpdateSessionAsync);
        api.MapDelete("/sessions/{id:guid}", DeleteSessionAsync);
        api.MapPost("/timesheet/start", StartTimesheetAsync);
        api.MapPost("/timesheet/end", EndTimesheetAsync);
        api.MapPut("/timesheet-entries/{id:guid}", UpdateTimesheetEntryAsync);
        api.MapDelete("/timesheet-entries/{id:guid}", DeleteTimesheetEntryAsync);
        api.MapGet("/unallocated", GetUnallocatedAsync);
        api.MapGet("/unallocated/activity", GetUnallocatedActivityAsync);
        api.MapGet("/unallocated/usage", GetUnallocatedUsageAsync);
        api.MapGet("/usage/imported", GetImportedUsageAsync);
        api.MapPost("/activity/assign", AssignActivityAsync);
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

    private static async Task<IResult> GetProjectTimesheetEntriesAsync(
        Guid id,
        ITimesheetManagementService timesheets,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            var list = await timesheets.ListForProjectAsync(id, fromUtc, toUtc, cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(list);
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
        var prompts = list
            .Where(e => e.EventType == ActivityEventType.PromptSubmitted)
            .OrderByDescending(e => e.TimestampUtc)
            .ToList();

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

        var usageByPrompt = linked
            .Where(a => a.ActivityEventId is Guid)
            .GroupBy(a => a.ActivityEventId!.Value)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    long tokens = 0;
                    decimal cost = 0m;
                    foreach (var attr in g)
                    {
                        if (attr.AllocatedTotalTokens > 0 || attr.AllocatedCost > 0)
                        {
                            tokens += attr.AllocatedTotalTokens;
                            cost += attr.AllocatedCost;
                            continue;
                        }

                        if (usageById.TryGetValue(attr.ExternalUsageRecordId, out var record))
                        {
                            tokens += record.TotalTokens
                                ?? ((record.InputTokens ?? 0) + (record.OutputTokens ?? 0)
                                    + (record.CachedInputTokens ?? 0) + (record.ReasoningTokens ?? 0));
                            cost += record.ReportedCost ?? 0m;
                        }
                    }

                    return (Tokens: tokens, Cost: cost, Count: g.Count(), Linked: true);
                });

        return Results.Ok(prompts.Select(e =>
        {
            usageByPrompt.TryGetValue(e.Id, out var linkedUsage);
            return new
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
                linkedUsageCount = linkedUsage.Linked ? linkedUsage.Count : 0,
                hasLinkedUsage = linkedUsage.Linked
            };
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
