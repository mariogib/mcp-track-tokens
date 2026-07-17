using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using McpTrackTokens.Domain.Entities;

namespace McpTrackTokens.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="ExternalUsageRecord"/>.
/// </summary>
public sealed class ExternalUsageRecordConfiguration : IEntityTypeConfiguration<ExternalUsageRecord>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ExternalUsageRecord> builder)
    {
        builder.ToTable("ExternalUsageRecords");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Source).HasConversion<string>().HasMaxLength(64);
        builder.Property(e => e.ExternalRecordId).HasMaxLength(512);
        builder.Property(e => e.UserIdentifier).HasMaxLength(512);
        builder.Property(e => e.Model).HasMaxLength(256);
        builder.Property(e => e.Provider).HasConversion<string>().HasMaxLength(64);
        builder.Property(e => e.ReportedCost).HasPrecision(18, 4);
        builder.Property(e => e.Currency).HasMaxLength(8);

        builder.HasIndex(e => e.TimestampUtc);
        builder.HasIndex(e => e.Source);
        builder.HasIndex(e => e.ImportBatchId);
        builder.HasIndex(e => e.ImportedAtUtc);
        builder.HasIndex(e => e.CreatedAtUtc);
        builder.HasIndex(e => e.ExternalRecordId);
        builder.HasIndex(e => new { e.Source, e.ExternalRecordId })
            .IsUnique()
            .HasFilter("\"ExternalRecordId\" IS NOT NULL");

        builder.HasOne<ImportBatch>()
            .WithMany()
            .HasForeignKey(e => e.ImportBatchId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
