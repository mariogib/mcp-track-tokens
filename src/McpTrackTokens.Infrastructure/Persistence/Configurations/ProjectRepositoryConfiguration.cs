using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using McpTrackTokens.Domain.Entities;

namespace McpTrackTokens.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="ProjectRepository"/>.
/// </summary>
public sealed class ProjectRepositoryConfiguration : IEntityTypeConfiguration<ProjectRepository>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ProjectRepository> builder)
    {
        builder.ToTable("ProjectRepositories");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.LocalPath).HasMaxLength(2048).IsRequired();
        builder.Property(e => e.NormalizedPath).HasMaxLength(2048).IsRequired();
        builder.Property(e => e.RemoteUrl).HasMaxLength(2048);
        builder.Property(e => e.NormalizedRemoteUrl).HasMaxLength(2048);
        builder.Property(e => e.DefaultBranch).HasMaxLength(256);

        builder.HasIndex(e => e.ProjectId).IsUnique();
        builder.HasIndex(e => e.NormalizedPath);
        builder.HasIndex(e => e.NormalizedRemoteUrl);
        builder.HasIndex(e => e.CreatedAtUtc);

        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(e => e.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
