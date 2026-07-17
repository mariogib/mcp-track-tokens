using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using McpTrackTokens.Domain.Entities;

namespace McpTrackTokens.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="Project"/>.
/// </summary>
public sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("Projects");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name).HasMaxLength(256).IsRequired();
        builder.Property(e => e.Slug).HasMaxLength(128).IsRequired();
        builder.Property(e => e.ClientName).HasMaxLength(256);
        builder.Property(e => e.BillingCode).HasMaxLength(128);
        builder.Property(e => e.Currency).HasMaxLength(8).IsRequired();
        builder.Property(e => e.PrimaryRepositoryPath).HasMaxLength(2048);
        builder.Property(e => e.PrimaryRemoteUrl).HasMaxLength(2048);

        builder.Property<byte[]>("RowVersion")
            .IsRequired()
            .IsConcurrencyToken()
            .HasDefaultValue(Array.Empty<byte>());

        builder.HasIndex(e => e.Slug).IsUnique();
        builder.HasIndex(e => e.ClientName);
        builder.HasIndex(e => e.CreatedAtUtc);
        builder.HasIndex(e => e.UpdatedAtUtc);
        builder.HasIndex(e => e.IsActive);
        builder.HasIndex(e => e.PrimaryRepositoryPath);
        builder.HasIndex(e => e.PrimaryRemoteUrl);
    }
}
