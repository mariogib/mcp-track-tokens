using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using McpTrackTokens.Domain.Entities;

namespace McpTrackTokens.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="TimesheetEntry"/>.
/// </summary>
public sealed class TimesheetEntryConfiguration : IEntityTypeConfiguration<TimesheetEntry>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<TimesheetEntry> builder)
    {
        builder.ToTable("TimesheetEntries");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Notes).HasMaxLength(8000);

        builder.HasIndex(e => e.ProjectId);
        builder.HasIndex(e => e.CategoryId);
        builder.HasIndex(e => e.StartedAtUtc);
        builder.HasIndex(e => e.EndedAtUtc);
        builder.HasIndex(e => new { e.ProjectId, e.EndedAtUtc });

        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(e => e.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<TimesheetCategory>()
            .WithMany()
            .HasForeignKey(e => e.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
