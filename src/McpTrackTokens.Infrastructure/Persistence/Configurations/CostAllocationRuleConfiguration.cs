using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using McpTrackTokens.Domain.Entities;

namespace McpTrackTokens.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="CostAllocationRule"/>.
/// </summary>
public sealed class CostAllocationRuleConfiguration : IEntityTypeConfiguration<CostAllocationRule>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<CostAllocationRule> builder)
    {
        builder.ToTable("CostAllocationRules");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name).HasMaxLength(256).IsRequired();
        builder.Property(e => e.RuleType).HasConversion<string>().HasMaxLength(64);
        builder.Property(e => e.ConfigurationJson);

        builder.HasIndex(e => e.IsEnabled);
        builder.HasIndex(e => e.Priority);
        builder.HasIndex(e => e.CreatedAtUtc);
        builder.HasIndex(e => e.UpdatedAtUtc);
    }
}
