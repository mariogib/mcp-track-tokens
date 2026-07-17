using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using McpTrackTokens.Domain.Entities;

namespace McpTrackTokens.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="ProjectAlias"/>.
/// </summary>
public sealed class ProjectAliasConfiguration : IEntityTypeConfiguration<ProjectAlias>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ProjectAlias> builder)
    {
        builder.ToTable("ProjectAliases");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Alias).HasMaxLength(1024).IsRequired();
        builder.Property(e => e.NormalizedAlias).HasMaxLength(1024).IsRequired();
        builder.Property(e => e.AliasType).HasConversion<string>().HasMaxLength(64);

        builder.HasIndex(e => e.ProjectId);
        builder.HasIndex(e => e.NormalizedAlias);
        builder.HasIndex(e => e.CreatedAtUtc);
        builder.HasIndex(e => new { e.NormalizedAlias, e.AliasType }).IsUnique();

        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(e => e.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
