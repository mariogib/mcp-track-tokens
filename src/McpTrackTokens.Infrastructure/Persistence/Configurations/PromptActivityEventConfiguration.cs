using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using McpTrackTokens.Domain.Entities;

namespace McpTrackTokens.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="PromptActivityEvent"/>.
/// </summary>
public sealed class PromptActivityEventConfiguration : IEntityTypeConfiguration<PromptActivityEvent>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PromptActivityEvent> builder)
    {
        builder.ToTable("PromptActivityEvents");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.EventType).HasConversion<string>().HasMaxLength(64);
        builder.Property(e => e.Editor).HasConversion<string>().HasMaxLength(64);
        builder.Property(e => e.ExternalEventId).HasMaxLength(512);
        builder.Property(e => e.ExternalConversationId).HasMaxLength(512);
        builder.Property(e => e.ExternalRequestId).HasMaxLength(512);
        builder.Property(e => e.WorkspacePath).HasMaxLength(2048);
        builder.Property(e => e.RepositoryPath).HasMaxLength(2048);
        builder.Property(e => e.RemoteUrl).HasMaxLength(2048);
        builder.Property(e => e.Branch).HasMaxLength(256);
        builder.Property(e => e.PromptHash).HasMaxLength(128);
        builder.Property(e => e.Model).HasMaxLength(256);
        builder.Property(e => e.Provider).HasConversion<string>().HasMaxLength(64);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(64);
        builder.Property(e => e.AttributionMethod).HasConversion<string>().HasMaxLength(64);
        builder.Property(e => e.AttributionConfidence).HasConversion<string>().HasMaxLength(64);

        builder.HasIndex(e => e.TimestampUtc);
        builder.HasIndex(e => e.ProjectId);
        builder.HasIndex(e => e.EditorSessionId);
        builder.HasIndex(e => e.ExternalEventId);
        builder.HasIndex(e => e.ExternalRequestId);
        builder.HasIndex(e => e.ExternalConversationId);
        builder.HasIndex(e => e.RepositoryPath);
        builder.HasIndex(e => e.CreatedAtUtc);
        builder.HasIndex(e => new { e.Editor, e.ExternalEventId })
            .IsUnique()
            .HasFilter("\"ExternalEventId\" IS NOT NULL");

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
