using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using McpTrackTokens.Domain.Entities;

namespace McpTrackTokens.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="PersistedAppSettings"/>.
/// </summary>
public sealed class PersistedAppSettingsConfiguration : IEntityTypeConfiguration<PersistedAppSettings>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PersistedAppSettings> builder)
    {
        builder.ToTable("AppSettings");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.PayloadJson).IsRequired();
    }
}
