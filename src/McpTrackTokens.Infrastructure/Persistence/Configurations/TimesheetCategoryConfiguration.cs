using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using McpTrackTokens.Domain.Entities;

namespace McpTrackTokens.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="TimesheetCategory"/>.
/// </summary>
public sealed class TimesheetCategoryConfiguration : IEntityTypeConfiguration<TimesheetCategory>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<TimesheetCategory> builder)
    {
        builder.ToTable("TimesheetCategories");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name).HasMaxLength(128).IsRequired();
        builder.HasIndex(e => e.Name).IsUnique();
        builder.HasIndex(e => e.SortOrder);
        builder.HasIndex(e => e.IsActive);
    }
}
