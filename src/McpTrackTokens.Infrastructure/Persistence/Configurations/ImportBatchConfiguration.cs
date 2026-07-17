using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using McpTrackTokens.Domain.Entities;

namespace McpTrackTokens.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="ImportBatch"/>.
/// </summary>
public sealed class ImportBatchConfiguration : IEntityTypeConfiguration<ImportBatch>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ImportBatch> builder)
    {
        builder.ToTable("ImportBatches");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Source).HasConversion<string>().HasMaxLength(64);
        builder.Property(e => e.FileName).HasMaxLength(1024);
        builder.Property(e => e.FileHash).HasMaxLength(128);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(64);
        builder.Property(e => e.ErrorSummary).HasMaxLength(8000);

        builder.HasIndex(e => e.FileHash)
            .IsUnique()
            .HasFilter("\"FileHash\" IS NOT NULL");
        builder.HasIndex(e => e.Source);
        builder.HasIndex(e => e.StartedAtUtc);
        builder.HasIndex(e => e.CompletedAtUtc);
        builder.HasIndex(e => e.CreatedAtUtc);
        builder.HasIndex(e => e.Status);
    }
}
