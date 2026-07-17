using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using McpTrackTokens.Domain.Entities;

namespace McpTrackTokens.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="ActivityWindow"/>.
/// </summary>
public sealed class ActivityWindowConfiguration : IEntityTypeConfiguration<ActivityWindow>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ActivityWindow> builder)
    {
        builder.ToTable("ActivityWindows");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.CalculationVersion).HasMaxLength(32).IsRequired();

        builder.HasIndex(e => e.ProjectId);
        builder.HasIndex(e => e.EditorSessionId);
        builder.HasIndex(e => e.StartedAtUtc);
        builder.HasIndex(e => e.EndedAtUtc);
        builder.HasIndex(e => e.CreatedAtUtc);
        builder.HasIndex(e => new { e.ProjectId, e.StartedAtUtc, e.EndedAtUtc });

        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(e => e.ProjectId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<EditorSession>()
            .WithMany()
            .HasForeignKey(e => e.EditorSessionId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
