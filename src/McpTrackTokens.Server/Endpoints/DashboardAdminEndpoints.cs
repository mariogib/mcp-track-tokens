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
        api.MapGet("/api-keys", ListApiKeysAsync);
        api.MapPost("/api-keys", CreateApiKeyAsync);
        api.MapDelete("/api-keys/{id:guid}", RevokeApiKeyAsync);
        api.MapGet("/integrations/status", GetIntegrationsAsync);

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

    private static async Task<IResult> UpdateSettingsAsync(
        UpdateSettingsRequestDto request,
        IOptions<TrackingOptions> optionsAccessor,
        ICursorTokenRateStore rateStore,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var options = optionsAccessor.Value;

        if (request.InactivityThresholdMinutes is int minutes && minutes > 0)
        {
            options.InactivityThresholdMinutes = minutes;
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

        var ratesChanged = false;
        if (request.EstimateCostFromTokenRates is bool estimate)
        {
            options.EstimateCostFromTokenRates = estimate;
            ratesChanged = true;
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

            ratesChanged = true;
        }

        if (ratesChanged)
        {
            await rateStore.SaveAsync(options, cancellationToken).ConfigureAwait(false);
        }

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

    private static async Task<IResult> GetIntegrationsAsync(
        IReportService reports,
        CancellationToken cancellationToken)
    {
        var status = await reports.GetTrackingStatusAsync(cancellationToken).ConfigureAwait(false);
        var queuePath = TrackingOptions.ExpandPath("~/.mcp-track-tokens/queue");
        var hooksPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cursor",
            "mcp-track-tokens-hooks");

        return Results.Ok(new
        {
            cursorHooksConfigured = Directory.Exists(hooksPath),
            vscodeExtensionDetected = false,
            mcpConfigured = true,
            lastIngestAtUtc = status.LastEventAtUtc,
            notes = new[]
            {
                status.QueuedEventCount > 0
                    ? $"{status.QueuedEventCount} queued offline event(s) under {queuePath}."
                    : "No queued offline events detected.",
                "VS Code extension presence is not auto-detected from the server process."
            }
        });
    }

    private static object ToSettingsDto(TrackingOptions options) => new
    {
        inactivityThresholdMinutes = options.InactivityThresholdMinutes,
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
