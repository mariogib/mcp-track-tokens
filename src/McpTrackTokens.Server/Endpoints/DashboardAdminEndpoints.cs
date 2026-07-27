using Microsoft.Extensions.Options;
using McpTrackTokens.Application.DTOs;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Application.Options;
using McpTrackTokens.Domain.Enums;
using McpTrackTokens.Infrastructure.Persistence;

namespace McpTrackTokens.Server.Endpoints;

/// <summary>
/// Dashboard administration endpoints (settings, API keys, integration status).
/// </summary>
public static class DashboardAdminEndpoints
{
    /// <summary>
    /// Maps settings, status, API key, and integration routes under <c>/api/v1</c>.
    /// </summary>
    public static IEndpointRouteBuilder MapDashboardAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1");

        api.MapGet("/status", GetStatusAsync);
        api.MapGet("/settings", GetSettings);
        api.MapPut("/settings", UpdateSettingsAsync);
        api.MapPost("/settings/cursor-token-rates/fetch", FetchCursorTokenRatesAsync);
        api.MapGet("/api-keys", ListApiKeysAsync);
        api.MapPost("/api-keys", CreateApiKeyAsync);
        api.MapDelete("/api-keys/{id:guid}", RevokeApiKeyAsync);
        api.MapGet("/timesheet-categories", ListTimesheetCategoriesAsync);
        api.MapPost("/timesheet-categories", CreateTimesheetCategoryAsync);
        api.MapPut("/timesheet-categories/{id:guid}", UpdateTimesheetCategoryAsync);
        api.MapDelete("/timesheet-categories/{id:guid}", DeleteTimesheetCategoryAsync);
        api.MapGet("/integrations/status", GetIntegrationsAsync);
        api.MapPost("/integrations/cursor-hooks/check", CheckCursorHooksAsync);
        api.MapPost("/integrations/offline-queue/replay", ReplayOfflineQueueAsync);
        api.MapGet("/database/backup-info", GetDatabaseBackupInfo);
        api.MapPost("/database/backup", BackupDatabaseAsync);
        api.MapGet("/database/backup-download", DownloadDatabaseBackupAsync);
        api.MapPost("/database/restore", RestoreDatabaseAsync);
        api.MapPost("/database/restore-upload", RestoreDatabaseUploadAsync);

        return app;
    }

    private static async Task<IResult> GetStatusAsync(
        IReportService reports,
        CancellationToken cancellationToken)
    {
        var status = await reports.GetTrackingStatusAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(status);
    }

    private static IResult GetSettings(IOptions<TrackingOptions> optionsAccessor)
    {
        return Results.Ok(ToSettingsDto(optionsAccessor.Value));
    }

    private static async Task<IResult> FetchCursorTokenRatesAsync(
        ICursorDocsPricingClient pricingClient,
        IOptions<TrackingOptions> optionsAccessor,
        ITrackingSettingsStore settingsStore,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await pricingClient.FetchRatesAsync(cancellationToken).ConfigureAwait(false);
            var options = optionsAccessor.Value;
            options.CursorTokenRates = result.Rates
                .Where(r => !string.IsNullOrWhiteSpace(r.Model))
                .Select(r => new CursorModelTokenRate
                {
                    Model = r.Model.Trim(),
                    InputPerMillion = Math.Max(0m, r.InputPerMillion),
                    OutputPerMillion = Math.Max(0m, r.OutputPerMillion),
                    CacheReadPerMillion = Math.Max(0m, r.CacheReadPerMillion),
                    CacheWritePerMillion = Math.Max(0m, r.CacheWritePerMillion),
                    ReasoningPerMillion = r.ReasoningPerMillion is null
                        ? null
                        : Math.Max(0m, r.ReasoningPerMillion.Value)
                })
                .GroupBy(r => r.Model, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.Last())
                .OrderBy(r => r.Model, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (options.CursorTokenRates.Count == 0)
            {
                options.CursorTokenRates = CursorTokenRateStore.CreateDefaultRates();
            }

            await settingsStore.SaveAsync(options, cancellationToken).ConfigureAwait(false);

            return Results.Ok(new
            {
                sourceUrl = result.SourceUrl,
                fetchedAtUtc = result.FetchedAtUtc,
                count = options.CursorTokenRates.Count,
                saved = true,
                warnings = result.Warnings,
                rates = options.CursorTokenRates.Select(r => new CursorModelTokenRateDto
                {
                    Model = r.Model,
                    InputPerMillion = r.InputPerMillion,
                    OutputPerMillion = r.OutputPerMillion,
                    CacheReadPerMillion = r.CacheReadPerMillion,
                    CacheWritePerMillion = r.CacheWritePerMillion,
                    ReasoningPerMillion = r.ReasoningPerMillion
                }).ToList()
            });
        }
        catch (Exception ex)
        {
            return Results.Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status502BadGateway,
                title: "Failed to fetch Cursor pricing");
        }
    }

    private static async Task<IResult> UpdateSettingsAsync(
        UpdateSettingsRequestDto request,
        IOptions<TrackingOptions> optionsAccessor,
        ITrackingSettingsStore settingsStore,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var options = optionsAccessor.Value;

        if (request.InactivityThresholdMinutes is int minutes && minutes > 0)
        {
            options.InactivityThresholdMinutes = minutes;
        }

        if (request.SessionInactivityCloseMinutes is int sessionMinutes && sessionMinutes > 0)
        {
            options.SessionInactivityCloseMinutes = sessionMinutes;
        }

        if (!string.IsNullOrWhiteSpace(request.DefaultCurrency))
        {
            options.DefaultCurrency = request.DefaultCurrency.Trim().ToUpperInvariant();
        }

        if (request.CursorSubscriptionAmount is decimal amount)
        {
            options.CursorSubscriptionAmount = amount;
        }

        if (!string.IsNullOrWhiteSpace(request.CursorSubscriptionCurrency))
        {
            options.CursorSubscriptionCurrency = request.CursorSubscriptionCurrency.Trim().ToUpperInvariant();
        }

        if (!string.IsNullOrWhiteSpace(request.CursorAllocationMethod) &&
            Enum.TryParse<AllocationRuleType>(request.CursorAllocationMethod, ignoreCase: true, out var method))
        {
            options.CursorAllocationMethod = method;
        }

        if (request.StorePromptContent is bool storePrompt)
        {
            options.StorePromptContent = storePrompt;
        }

        if (request.StoreResponseContent is bool storeResponse)
        {
            options.StoreResponseContent = storeResponse;
        }

        if (request.EnablePromptHashing is bool hashing)
        {
            options.EnablePromptHashing = hashing;
        }

        if (!string.IsNullOrWhiteSpace(request.ExportPath))
        {
            options.ExportPath = request.ExportPath.Trim();
        }

        if (request.AutoCreateProjects is bool autoCreate)
        {
            options.AutoCreateProjects = autoCreate;
        }

        if (request.DataRetentionDays is int retention)
        {
            options.DataRetentionDays = retention > 0 ? retention : null;
        }
        else if (request.ClearDataRetentionDays)
        {
            options.DataRetentionDays = null;
        }

        if (request.EstimateCostFromTokenRates is bool estimate)
        {
            options.EstimateCostFromTokenRates = estimate;
        }

        if (request.CursorTokenRates is not null)
        {
            options.CursorTokenRates = request.CursorTokenRates
                .Where(r => !string.IsNullOrWhiteSpace(r.Model))
                .Select(r => new CursorModelTokenRate
                {
                    Model = r.Model.Trim(),
                    InputPerMillion = Math.Max(0m, r.InputPerMillion),
                    OutputPerMillion = Math.Max(0m, r.OutputPerMillion),
                    CacheReadPerMillion = Math.Max(0m, r.CacheReadPerMillion),
                    CacheWritePerMillion = Math.Max(0m, r.CacheWritePerMillion),
                    ReasoningPerMillion = r.ReasoningPerMillion is null
                        ? null
                        : Math.Max(0m, r.ReasoningPerMillion.Value)
                })
                .ToList();
            if (options.CursorTokenRates.Count == 0)
            {
                options.CursorTokenRates = CursorTokenRateStore.CreateDefaultRates();
            }
        }

        await settingsStore.SaveAsync(options, cancellationToken).ConfigureAwait(false);

        return Results.Ok(ToSettingsDto(options));
    }

    private static async Task<IResult> ListApiKeysAsync(
        IApiKeyService apiKeys,
        CancellationToken cancellationToken)
    {
        var keys = await apiKeys.ListAsync(activeOnly: false, cancellationToken).ConfigureAwait(false);
        return Results.Ok(keys.Select(k => new
        {
            id = k.Id,
            name = k.Name,
            createdAtUtc = k.CreatedAtUtc,
            expiresAtUtc = k.ExpiresAtUtc,
            lastUsedAtUtc = k.LastUsedAtUtc,
            isActive = k.IsActive,
            allowedEditors = k.AllowedEditors,
            allowedMachineNames = k.AllowedMachineNames
        }));
    }

    private static async Task<IResult> CreateApiKeyAsync(
        CreateApiKeyRequestDto request,
        IApiKeyService apiKeys,
        CancellationToken cancellationToken)
    {
        try
        {
            var created = await apiKeys.CreateAsync(request, cancellationToken).ConfigureAwait(false);
            return Results.Ok(created);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> RevokeApiKeyAsync(
        Guid id,
        IApiKeyService apiKeys,
        CancellationToken cancellationToken)
    {
        try
        {
            await apiKeys.RevokeAsync(id, cancellationToken).ConfigureAwait(false);
            return Results.NoContent();
        }
        catch (Exception ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
    }

    private static async Task<IResult> ListTimesheetCategoriesAsync(
        ITimesheetCategoryService categories,
        bool? activeOnly,
        CancellationToken cancellationToken)
    {
        var list = await categories
            .ListAsync(activeOnly ?? false, cancellationToken)
            .ConfigureAwait(false);
        return Results.Ok(list);
    }

    private static async Task<IResult> CreateTimesheetCategoryAsync(
        CreateTimesheetCategoryRequest request,
        ITimesheetCategoryService categories,
        CancellationToken cancellationToken)
    {
        try
        {
            var created = await categories.CreateAsync(request, cancellationToken).ConfigureAwait(false);
            return Results.Created($"/api/v1/timesheet-categories/{created.Id}", created);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> UpdateTimesheetCategoryAsync(
        Guid id,
        UpdateTimesheetCategoryRequest request,
        ITimesheetCategoryService categories,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await categories.UpdateAsync(id, request, cancellationToken).ConfigureAwait(false);
            return Results.Ok(updated);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> DeleteTimesheetCategoryAsync(
        Guid id,
        ITimesheetCategoryService categories,
        CancellationToken cancellationToken)
    {
        try
        {
            await categories.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
            return Results.NoContent();
        }
        catch (Exception ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
    }

    private static async Task<IResult> GetIntegrationsAsync(
        IReportService reports,
        IActivityEventRepository events,
        CancellationToken cancellationToken)
    {
        var status = await reports.GetTrackingStatusAsync(cancellationToken).ConfigureAwait(false);
        var queuePath = TrackingOptions.ExpandPath("~/.mcp-track-tokens/queue");
        var hooksPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cursor",
            "mcp-track-tokens-hooks");

        var hooksOnDisk = Directory.Exists(hooksPath);
        var now = DateTimeOffset.UtcNow;
        var recentEvents = await events
            .ListAsync(now.AddDays(-14), now, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var recentCursorIngest = recentEvents.Any(e => e.Editor == EditorType.Cursor);
        var cursorHooksConfigured = hooksOnDisk || recentCursorIngest;

        var notes = new List<string>
        {
            status.QueuedEventCount > 0
                ? $"{status.QueuedEventCount} queued offline event(s) under {queuePath}."
                : "No queued offline events detected."
        };

        if (hooksOnDisk)
        {
            notes.Add($"Cursor hooks directory found at {hooksPath}.");
        }
        else if (recentCursorIngest)
        {
            notes.Add(
                "Cursor hooks directory is not visible to this process (common when the API runs in Docker). " +
                "Marked configured because recent Cursor events were ingested.");
        }
        else
        {
            notes.Add(
                $"Cursor hooks directory not found at {hooksPath}. " +
                "Install with: mcp-track-tokens install-cursor-hooks");
        }

        return Results.Ok(new
        {
            cursorHooksConfigured,
            cursorHooksOnDisk = hooksOnDisk,
            cursorHooksInferredFromActivity = !hooksOnDisk && recentCursorIngest,
            mcpConfigured = true,
            lastIngestAtUtc = status.LastEventAtUtc,
            notes
        });
    }

    private static async Task<IResult> CheckCursorHooksAsync(
        ICursorHooksCompatibilityService hooksCompatibility,
        CancellationToken cancellationToken)
    {
        try
        {
            var report = await hooksCompatibility.CheckAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(report);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> ReplayOfflineQueueAsync(
        IOfflineQueueReplayService replay,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await replay.ReplayAsync(cancellationToken).ConfigureAwait(false);
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static IResult GetDatabaseBackupInfo(
        IDatabaseBackupService backups,
        [Microsoft.AspNetCore.Mvc.FromQuery] string? destinationDirectory)
    {
        try
        {
            return Results.Ok(backups.GetInfo(destinationDirectory));
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> BackupDatabaseAsync(
        DatabaseBackupRequestDto? request,
        IDatabaseBackupService backups,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await backups
                .BackupAsync(request?.DestinationDirectory, cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> DownloadDatabaseBackupAsync(
        IDatabaseBackupService backups,
        CancellationToken cancellationToken)
    {
        try
        {
            var (stream, fileName) = await backups
                .CreateDownloadableBackupAsync(cancellationToken)
                .ConfigureAwait(false);
            return Results.File(stream, "application/x-sqlite3", fileName);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> RestoreDatabaseAsync(
        DatabaseRestoreRequestDto request,
        IDatabaseBackupService backups,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.SourceFilePath))
        {
            return Results.BadRequest(new { error = "SourceFilePath is required." });
        }

        try
        {
            var result = await backups
                .RestoreAsync(request.SourceFilePath, cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> RestoreDatabaseUploadAsync(
        HttpRequest httpRequest,
        IDatabaseBackupService backups,
        CancellationToken cancellationToken)
    {
        if (!httpRequest.HasFormContentType)
        {
            return Results.BadRequest(new { error = "Expected multipart form upload with a 'file' field." });
        }

        var form = await httpRequest.ReadFormAsync(cancellationToken).ConfigureAwait(false);
        var file = form.Files.GetFile("file");
        if (file is null || file.Length <= 0)
        {
            return Results.BadRequest(new { error = "Upload a SQLite .db backup file." });
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"mcp-track-tokens-restore-{Guid.NewGuid():N}.db");
        try
        {
            await using (var stream = File.Create(tempPath))
            {
                await file.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
            }

            var result = await backups.RestoreAsync(tempPath, cancellationToken).ConfigureAwait(false);
            return Results.Ok(result with
            {
                RestoredFromPath = file.FileName,
                Message = result.Message + " (restored from uploaded file)."
            });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
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
                // ignore temp cleanup failures
            }
        }
    }

    private static object ToSettingsDto(TrackingOptions options) => new
    {
        inactivityThresholdMinutes = options.InactivityThresholdMinutes,
        sessionInactivityCloseMinutes = options.SessionInactivityCloseMinutes,
        defaultCurrency = options.DefaultCurrency,
        cursorSubscriptionAmount = options.CursorSubscriptionAmount,
        cursorSubscriptionCurrency = options.CursorSubscriptionCurrency,
        cursorAllocationMethod = options.CursorAllocationMethod.ToString(),
        storePromptContent = options.StorePromptContent,
        storeResponseContent = options.StoreResponseContent,
        enablePromptHashing = options.EnablePromptHashing,
        exportPath = options.ExportPath,
        databasePath = options.GetResolvedDatabasePath(),
        databaseProvider = options.DatabaseProvider,
        dataRetentionDays = options.DataRetentionDays,
        serverUrl = options.ServerUrl,
        autoCreateProjects = options.AutoCreateProjects,
        estimateCostFromTokenRates = options.EstimateCostFromTokenRates,
        cursorTokenRates = options.CursorTokenRates.Select(r => new
        {
            model = r.Model,
            inputPerMillion = r.InputPerMillion,
            outputPerMillion = r.OutputPerMillion,
            cacheReadPerMillion = r.CacheReadPerMillion,
            cacheWritePerMillion = r.CacheWritePerMillion,
            reasoningPerMillion = r.ReasoningPerMillion
        })
    };
}

/// <summary>
/// Partial update payload for tracking settings.
/// </summary>
public sealed class UpdateSettingsRequestDto
{
    public int? InactivityThresholdMinutes { get; set; }

    public int? SessionInactivityCloseMinutes { get; set; }

    public string? DefaultCurrency { get; set; }

    public decimal? CursorSubscriptionAmount { get; set; }

    public string? CursorSubscriptionCurrency { get; set; }

    public string? CursorAllocationMethod { get; set; }

    public bool? StorePromptContent { get; set; }

    public bool? StoreResponseContent { get; set; }

    public bool? EnablePromptHashing { get; set; }

    public string? ExportPath { get; set; }

    public int? DataRetentionDays { get; set; }

    public bool ClearDataRetentionDays { get; set; }

    public bool? AutoCreateProjects { get; set; }

    public bool? EstimateCostFromTokenRates { get; set; }

    public List<CursorModelTokenRateDto>? CursorTokenRates { get; set; }
}

/// <summary>
/// One Cursor model rate row for settings updates.
/// </summary>
public sealed class CursorModelTokenRateDto
{
    public string Model { get; set; } = string.Empty;

    public decimal InputPerMillion { get; set; }

    public decimal OutputPerMillion { get; set; }

    public decimal CacheReadPerMillion { get; set; }

    public decimal CacheWritePerMillion { get; set; }

    public decimal? ReasoningPerMillion { get; set; }
}
