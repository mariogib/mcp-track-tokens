using Microsoft.EntityFrameworkCore;
using McpTrackTokens.Domain.Entities;

namespace McpTrackTokens.Infrastructure.Persistence;

/// <summary>
/// EF Core database context for tracking entities.
/// </summary>
public sealed class TrackingDbContext : DbContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TrackingDbContext"/> class.
    /// </summary>
    public TrackingDbContext(DbContextOptions<TrackingDbContext> options)
        : base(options)
    {
    }

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<ProjectRepository> ProjectRepositories => Set<ProjectRepository>();

    public DbSet<ProjectAlias> ProjectAliases => Set<ProjectAlias>();

    public DbSet<EditorSession> EditorSessions => Set<EditorSession>();

    public DbSet<TimesheetEntry> TimesheetEntries => Set<TimesheetEntry>();

    public DbSet<PromptActivityEvent> PromptActivityEvents => Set<PromptActivityEvent>();

    public DbSet<ActivityWindow> ActivityWindows => Set<ActivityWindow>();

    public DbSet<ExternalUsageRecord> ExternalUsageRecords => Set<ExternalUsageRecord>();

    public DbSet<UsageAttribution> UsageAttributions => Set<UsageAttribution>();

    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();

    public DbSet<CostAllocationRule> CostAllocationRules => Set<CostAllocationRule>();

    public DbSet<TrackingApiKey> TrackingApiKeys => Set<TrackingApiKey>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TrackingDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    /// <inheritdoc />
    public override int SaveChanges()
    {
        StampRowVersions();
        return base.SaveChanges();
    }

    /// <inheritdoc />
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampRowVersions();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void StampRowVersions()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is not (Project or EditorSession))
            {
                continue;
            }

            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Property("RowVersion").CurrentValue = Guid.NewGuid().ToByteArray();
            }
        }
    }
}
