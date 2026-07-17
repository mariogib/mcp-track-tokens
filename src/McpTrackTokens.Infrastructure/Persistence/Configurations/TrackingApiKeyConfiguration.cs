using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using McpTrackTokens.Domain.Entities;

namespace McpTrackTokens.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="TrackingApiKey"/>.
/// </summary>
public sealed class TrackingApiKeyConfiguration : IEntityTypeConfiguration<TrackingApiKey>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<TrackingApiKey> builder)
    {
        builder.ToTable("TrackingApiKeys");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name).HasMaxLength(256).IsRequired();
        builder.Property(e => e.KeyHash).HasMaxLength(128).IsRequired();
        builder.Property(e => e.AllowedEditors).HasMaxLength(512);
        builder.Property(e => e.AllowedMachineNames).HasMaxLength(1024);

        builder.HasIndex(e => e.KeyHash).IsUnique();
        builder.HasIndex(e => e.IsActive);
        builder.HasIndex(e => e.CreatedAtUtc);
        builder.HasIndex(e => e.ExpiresAtUtc);
        builder.HasIndex(e => e.LastUsedAtUtc);
    }
}
