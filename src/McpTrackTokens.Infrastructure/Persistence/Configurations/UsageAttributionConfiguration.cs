using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using McpTrackTokens.Domain.Entities;

namespace McpTrackTokens.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="UsageAttribution"/>.
/// </summary>
public sealed class UsageAttributionConfiguration : IEntityTypeConfiguration<UsageAttribution>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<UsageAttribution> builder)
    {
        builder.ToTable("UsageAttributions");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.AllocatedCost).HasPrecision(18, 4);
        builder.Property(e => e.AllocationPercentage).HasPrecision(18, 4);
        builder.Property(e => e.AttributionMethod).HasConversion<string>().HasMaxLength(64);
        builder.Property(e => e.Confidence).HasConversion<string>().HasMaxLength(64);
        builder.Property(e => e.Reason).HasMaxLength(2048);
        builder.Property(e => e.ReviewedBy).HasMaxLength(256);

        builder.HasIndex(e => e.ExternalUsageRecordId);
        builder.HasIndex(e => e.ProjectId);
        builder.HasIndex(e => e.EditorSessionId);
        // Non-unique: many usage attributions may reference the same prompt.
        builder.HasIndex(e => e.ActivityEventId);
        builder.HasIndex(e => e.CreatedAtUtc);
        builder.HasIndex(e => e.ReviewedAtUtc);

        builder.HasOne<ExternalUsageRecord>()
            .WithMany()
            .HasForeignKey(e => e.ExternalUsageRecordId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(e => e.ProjectId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<EditorSession>()
            .WithMany()
            .HasForeignKey(e => e.EditorSessionId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<PromptActivityEvent>()
            .WithMany()
            .HasForeignKey(e => e.ActivityEventId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
