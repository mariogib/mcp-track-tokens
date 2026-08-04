using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using McpTrackTokens.Domain.Entities;

namespace McpTrackTokens.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="EditorSession"/>.
/// </summary>
public sealed class EditorSessionConfiguration : IEntityTypeConfiguration<EditorSession>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<EditorSession> builder)
    {
        builder.ToTable("EditorSessions");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Editor).HasConversion<string>().HasMaxLength(64);
        builder.Property(e => e.EditorVersion).HasMaxLength(128);
        builder.Property(e => e.MachineName).HasMaxLength(256);
        builder.Property(e => e.UserName).HasMaxLength(256);
        builder.Property(e => e.WorkspacePath).HasMaxLength(2048);
        builder.Property(e => e.RepositoryPath).HasMaxLength(2048);
        builder.Property(e => e.RemoteUrl).HasMaxLength(2048);
        builder.Property(e => e.Branch).HasMaxLength(256);
        builder.Property(e => e.ExternalSessionId).HasMaxLength(512);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(64);

        builder.Property<byte[]>("RowVersion")
            .IsRequired()
            .IsConcurrencyToken()
            .HasDefaultValue(Array.Empty<byte>());

        builder.HasIndex(e => e.StartedAtUtc);
        builder.HasIndex(e => e.EndedAtUtc);
        builder.HasIndex(e => e.LastActivityAtUtc);
        builder.HasIndex(e => new { e.Status, e.LastActivityAtUtc });
        builder.HasIndex(e => new { e.ProjectId, e.StartedAtUtc });
        builder.HasIndex(e => new { e.Editor, e.ExternalSessionId });

        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(e => e.ProjectId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
