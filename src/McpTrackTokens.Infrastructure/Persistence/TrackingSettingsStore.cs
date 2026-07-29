using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Application.Options;
using McpTrackTokens.Domain.Entities;
using McpTrackTokens.Domain.Enums;

namespace McpTrackTokens.Infrastructure.Persistence;

/// <summary>
/// Database-backed store for dashboard tracking settings (singleton JSON row).
/// </summary>
public sealed class TrackingSettingsStore : ITrackingSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly TrackingDbContext _db;
    private readonly ICursorTokenRateStore _rateFileStore;
    private readonly ILogger<TrackingSettingsStore> _logger;

    public TrackingSettingsStore(
        TrackingDbContext db,
        ICursorTokenRateStore rateFileStore,
        ILogger<TrackingSettingsStore> logger)
    {
        _db = db;
        _rateFileStore = rateFileStore;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task LoadIntoAsync(TrackingOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Always seed rate defaults / legacy JSON file first so empty DB installs still have rates.
        await _rateFileStore.LoadIntoAsync(options, cancellationToken).ConfigureAwait(false);

        PersistedAppSettings? row;
        try
        {
            row = await _db.AppSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == PersistedAppSettings.SingletonId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read AppSettings; using configuration defaults");
            return;
        }

        if (row is null || string.IsNullOrWhiteSpace(row.PayloadJson) || row.PayloadJson == "{}")
        {
            // First run after upgrade: persist current options (incl. rates from JSON) into DB.
            await SaveAsync(options, cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            var document = JsonSerializer.Deserialize<TrackingSettingsDocument>(row.PayloadJson, JsonOptions);
            if (document is null)
            {
                return;
            }

            ApplyDocument(options, document);
            if (options.CursorTokenRates.Count == 0)
            {
                options.CursorTokenRates = CursorTokenRateStore.CreateDefaultRates();
            }
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            _logger.LogWarning(ex, "Failed to deserialize AppSettings payload");
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(TrackingOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var document = ToDocument(options);
        var payload = JsonSerializer.Serialize(document, JsonOptions);

        var row = await _db.AppSettings
            .FirstOrDefaultAsync(s => s.Id == PersistedAppSettings.SingletonId, cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            await _db.AppSettings
                .AddAsync(PersistedAppSettings.Create(payload), cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            row.ReplacePayload(payload);
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Keep the legacy rates file in sync so older tooling still sees the rate card.
        try
        {
            await _rateFileStore.SaveAsync(options, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Persisted settings to database but failed to mirror Cursor rates file");
        }
    }

    private static TrackingSettingsDocument ToDocument(TrackingOptions options) => new()
    {
        InactivityThresholdMinutes = options.InactivityThresholdMinutes,
        SessionInactivityCloseMinutes = options.SessionInactivityCloseMinutes,
        DefaultCurrency = options.DefaultCurrency,
        CursorSubscriptionAmount = options.CursorSubscriptionAmount,
        CursorSubscriptionCurrency = options.CursorSubscriptionCurrency,
        CursorAllocationMethod = options.CursorAllocationMethod,
        StorePromptContent = options.StorePromptContent,
        StoreResponseContent = options.StoreResponseContent,
        EnablePromptHashing = options.EnablePromptHashing,
        ExportPath = options.ExportPath,
        DataRetentionDays = options.DataRetentionDays,
        AutoCreateProjects = options.AutoCreateProjects,
        EstimateCostFromTokenRates = options.EstimateCostFromTokenRates,
        CursorTokenRates = options.CursorTokenRates
            .Select(r => new CursorModelTokenRate
            {
                Model = r.Model,
                InputPerMillion = r.InputPerMillion,
                OutputPerMillion = r.OutputPerMillion,
                CacheReadPerMillion = r.CacheReadPerMillion,
                CacheWritePerMillion = r.CacheWritePerMillion,
                ReasoningPerMillion = r.ReasoningPerMillion
            })
            .ToList()
    };

    private static void ApplyDocument(TrackingOptions options, TrackingSettingsDocument document)
    {
        if (document.InactivityThresholdMinutes is int inactivity && inactivity > 0)
        {
            options.InactivityThresholdMinutes = inactivity;
        }

        if (document.SessionInactivityCloseMinutes is int sessionClose && sessionClose > 0)
        {
            options.SessionInactivityCloseMinutes = sessionClose;
        }

        if (!string.IsNullOrWhiteSpace(document.DefaultCurrency))
        {
            options.DefaultCurrency = document.DefaultCurrency.Trim().ToUpperInvariant();
        }

        if (document.CursorSubscriptionAmount is decimal amount)
        {
            options.CursorSubscriptionAmount = amount;
        }

        if (!string.IsNullOrWhiteSpace(document.CursorSubscriptionCurrency))
        {
            options.CursorSubscriptionCurrency = document.CursorSubscriptionCurrency.Trim().ToUpperInvariant();
        }

        if (document.CursorAllocationMethod is AllocationRuleType method)
        {
            options.CursorAllocationMethod = method;
        }

        if (document.StorePromptContent is bool storePrompt)
        {
            options.StorePromptContent = storePrompt;
        }

        if (document.StoreResponseContent is bool storeResponse)
        {
            options.StoreResponseContent = storeResponse;
        }

        if (document.EnablePromptHashing is bool hashing)
        {
            options.EnablePromptHashing = hashing;
        }

        if (!string.IsNullOrWhiteSpace(document.ExportPath))
        {
            options.ExportPath = document.ExportPath.Trim();
        }

        options.DataRetentionDays = document.DataRetentionDays is int days && days > 0 ? days : null;

        if (document.AutoCreateProjects is bool autoCreate)
        {
            options.AutoCreateProjects = autoCreate;
        }

        if (document.EstimateCostFromTokenRates is bool estimate)
        {
            options.EstimateCostFromTokenRates = estimate;
        }

        if (document.CursorTokenRates is { Count: > 0 })
        {
            options.CursorTokenRates = document.CursorTokenRates;
        }
    }

    private sealed class TrackingSettingsDocument
    {
        public int? InactivityThresholdMinutes { get; set; }

        public int? SessionInactivityCloseMinutes { get; set; }

        public string? DefaultCurrency { get; set; }

        public decimal? CursorSubscriptionAmount { get; set; }

        public string? CursorSubscriptionCurrency { get; set; }

        public AllocationRuleType? CursorAllocationMethod { get; set; }

        public bool? StorePromptContent { get; set; }

        public bool? StoreResponseContent { get; set; }

        public bool? EnablePromptHashing { get; set; }

        public string? ExportPath { get; set; }

        public int? DataRetentionDays { get; set; }

        public bool? AutoCreateProjects { get; set; }

        public bool? EstimateCostFromTokenRates { get; set; }

        public List<CursorModelTokenRate>? CursorTokenRates { get; set; }
    }
}
