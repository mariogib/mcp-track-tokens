using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Application.Options;
using McpTrackTokens.Application.Services;
using McpTrackTokens.Domain.Services;

namespace McpTrackTokens.Application.DependencyInjection;

/// <summary>
/// Registers application services and validators.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds MCP Track Tokens application-layer services.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services
            .AddOptions<TrackingOptions>()
            .BindConfiguration(TrackingOptions.SectionName)
            .Validate(o => o.InactivityThresholdMinutes > 0, "InactivityThresholdMinutes must be positive.")
            .Validate(o => o.MaxMetadataBytes > 0, "MaxMetadataBytes must be positive.")
            .ValidateOnStart();

        services.TryAddSingleton<ActivityWindowCalculator>();
        services.TryAddSingleton<CostAllocationCalculator>();
        services.TryAddSingleton<SubscriptionAllocationCalculator>();

        services.TryAddSingleton<IPathNormalizer, PathNormalizer>();
        services.TryAddSingleton<IFileHashService, FileHashService>();

        services.TryAddScoped<IEventIngestionService, EventIngestionService>();
        services.TryAddScoped<IActivityWindowService, ActivityWindowService>();
        services.TryAddScoped<IAttributionEngine, AttributionEngine>();
        services.TryAddScoped<ISubscriptionAllocationService, SubscriptionAllocationService>();
        services.TryAddScoped<IProjectDetectionService, ProjectDetectionService>();
        services.TryAddScoped<ISessionManagementService, SessionManagementService>();
        services.TryAddScoped<ITimesheetManagementService, TimesheetManagementService>();
        services.TryAddScoped<ITimesheetCategoryService, TimesheetCategoryService>();
        services.TryAddScoped<ITimesheetReportService, TimesheetReportService>();
        services.TryAddScoped<IReportService, ReportService>();
        services.TryAddScoped<IReconciliationService, ReconciliationService>();
        services.TryAddScoped<IExportService, ExportService>();
        services.TryAddScoped<IApiKeyService, ApiKeyService>();
        services.TryAddScoped<ICursorHooksCompatibilityService, CursorHooksCompatibilityService>();
        services.TryAddScoped<IOfflineQueueReplayService, OfflineQueueReplayService>();

        services.AddValidatorsFromAssemblyContaining<Validators.IngestEventDtoValidator>();

        return services;
    }
}
