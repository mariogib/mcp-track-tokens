using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Application.Options;
using McpTrackTokens.Infrastructure.Background;
using McpTrackTokens.Infrastructure.Export;
using McpTrackTokens.Infrastructure.Git;
using McpTrackTokens.Infrastructure.Import;
using McpTrackTokens.Infrastructure.Persistence;
using McpTrackTokens.Infrastructure.Pricing;
using McpTrackTokens.Infrastructure.Repositories;
using McpTrackTokens.Infrastructure.Security;

namespace McpTrackTokens.Infrastructure.DependencyInjection;

/// <summary>
/// Registers infrastructure services for MCP Track Tokens.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds EF Core persistence, repositories, git resolution, import/export, and encryption.
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<TrackingOptions>()
            .Bind(configuration.GetSection(TrackingOptions.SectionName))
            .Validate(o => o.InactivityThresholdMinutes > 0, "InactivityThresholdMinutes must be positive.")
            .ValidateOnStart();

        services.AddSingleton<SqliteForeignKeyInterceptor>();

        services.AddDbContext<TrackingDbContext>((sp, options) =>
        {
            var tracking = sp.GetRequiredService<IOptions<TrackingOptions>>().Value;
            ConfigureDbContext(options, tracking, sp.GetRequiredService<SqliteForeignKeyInterceptor>());
        });

        services.TryAddScoped<IProjectRepository, ProjectRepository>();
        services.TryAddScoped<ISessionRepository, SessionRepository>();
        services.TryAddScoped<ITimesheetEntryRepository, TimesheetEntryRepository>();
        services.TryAddScoped<ITimesheetCategoryRepository, TimesheetCategoryRepository>();
        services.TryAddScoped<IActivityEventRepository, ActivityEventRepository>();
        services.TryAddScoped<IActivityWindowRepository, ActivityWindowRepository>();
        services.TryAddScoped<IExternalUsageRepository, ExternalUsageRepository>();
        services.TryAddScoped<IUsageAttributionRepository, UsageAttributionRepository>();
        services.TryAddScoped<IImportBatchRepository, ImportBatchRepository>();
        services.TryAddScoped<ICostAllocationRuleRepository, CostAllocationRuleRepository>();
        services.TryAddScoped<IApiKeyRepository, ApiKeyRepository>();
        services.TryAddScoped<IUnitOfWork, UnitOfWork>();

        services.TryAddSingleton<IGitRepositoryResolver, GitRepositoryResolver>();
        services.TryAddSingleton<ICursorUsageFormatDetector, CursorUsageFormatDetector>();
        services.TryAddSingleton<ICursorUsageColumnMapper, CursorUsageColumnMapper>();
        services.TryAddSingleton<ICursorTokenRateStore, CursorTokenRateStore>();
        services.AddHttpClient<ICursorDocsPricingClient, CursorDocsPricingClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.TryAddScoped<IExternalUsageNormalizer, ExternalUsageNormalizer>();
        services.TryAddScoped<ICursorUsageImporter, CursorUsageImporter>();
        services.TryAddScoped<IReportExporter, ReportExporter>();
        services.TryAddSingleton<IContentEncryptionService, ContentEncryptionService>();
        services.TryAddSingleton<IDatabaseBackupService, DatabaseBackupService>();

        services.AddHostedService<QueuedEventFlushService>();
        services.AddHostedService<ReconciliationBackgroundService>();

        return services;
    }

    private static void ConfigureDbContext(
        DbContextOptionsBuilder options,
        TrackingOptions tracking,
        SqliteForeignKeyInterceptor sqliteInterceptor)
    {
        var provider = tracking.DatabaseProvider?.Trim() ?? "Sqlite";
        if (provider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase) ||
            provider.Equals("Npgsql", StringComparison.OrdinalIgnoreCase) ||
            provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(tracking.ConnectionString))
            {
                throw new InvalidOperationException(
                    "Tracking:ConnectionString is required when DatabaseProvider is PostgreSQL.");
            }

            options.UseNpgsql(tracking.ConnectionString);
            return;
        }

        var databasePath = tracking.GetResolvedDatabasePath();
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        options.UseSqlite($"Data Source={databasePath}");
        options.AddInterceptors(sqliteInterceptor);
    }
}
