using McpTrackTokens.Domain.Common;
using McpTrackTokens.Domain.Enums;

namespace McpTrackTokens.Domain.Entities;

/// <summary>
/// A batch of imported usage records from a single file or API pull.
/// </summary>
public sealed class ImportBatch : EntityBase
{
    /// <summary>
    /// Gets or sets the usage source.
    /// </summary>
    public UsageSource Source { get; set; }

    /// <summary>
    /// Gets or sets the original file name, when applicable.
    /// </summary>
    public string? FileName { get; set; }

    /// <summary>
    /// Gets or sets the content hash of the imported file.
    /// </summary>
    public string? FileHash { get; set; }

    /// <summary>
    /// Gets or sets when the import started in UTC.
    /// </summary>
    public DateTimeOffset StartedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets when the import completed in UTC.
    /// </summary>
    public DateTimeOffset? CompletedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the number of records received.
    /// </summary>
    public int ReceivedCount { get; set; }

    /// <summary>
    /// Gets or sets the number of records imported.
    /// </summary>
    public int ImportedCount { get; set; }

    /// <summary>
    /// Gets or sets the number of duplicate records skipped.
    /// </summary>
    public int DuplicateCount { get; set; }

    /// <summary>
    /// Gets or sets the number of failed records.
    /// </summary>
    public int FailedCount { get; set; }

    /// <summary>
    /// Gets or sets the import status.
    /// </summary>
    public ImportStatus Status { get; set; } = ImportStatus.Pending;

    /// <summary>
    /// Gets or sets a summary of errors, when any occurred.
    /// </summary>
    public string? ErrorSummary { get; set; }

    /// <summary>
    /// Creates a new import batch in the pending state.
    /// </summary>
    public static ImportBatch Create(
        UsageSource source,
        string? fileName = null,
        string? fileHash = null,
        DateTimeOffset? startedAtUtc = null,
        Guid? id = null)
    {
        var started = startedAtUtc?.ToUniversalTime() ?? DateTimeOffset.UtcNow;
        return new ImportBatch(id ?? Guid.NewGuid(), started)
        {
            Source = source,
            FileName = fileName,
            FileHash = fileHash,
            StartedAtUtc = started,
            Status = ImportStatus.Pending
        };
    }

    /// <summary>
    /// Marks the batch as in progress.
    /// </summary>
    public void MarkInProgress()
    {
        Status = ImportStatus.InProgress;
    }

    /// <summary>
    /// Completes the batch with final counters.
    /// </summary>
    public void Complete(
        int receivedCount,
        int importedCount,
        int duplicateCount,
        int failedCount,
        DateTimeOffset? completedAtUtc = null)
    {
        Guard.AgainstNegative(receivedCount);
        Guard.AgainstNegative(importedCount);
        Guard.AgainstNegative(duplicateCount);
        Guard.AgainstNegative(failedCount);

        ReceivedCount = receivedCount;
        ImportedCount = importedCount;
        DuplicateCount = duplicateCount;
        FailedCount = failedCount;
        CompletedAtUtc = completedAtUtc?.ToUniversalTime() ?? DateTimeOffset.UtcNow;
        Status = failedCount > 0 && importedCount > 0
            ? ImportStatus.Partial
            : failedCount > 0
                ? ImportStatus.Failed
                : ImportStatus.Completed;
    }

    /// <summary>
    /// Marks the batch as failed.
    /// </summary>
    public void Fail(string? errorSummary, DateTimeOffset? completedAtUtc = null)
    {
        ErrorSummary = errorSummary;
        CompletedAtUtc = completedAtUtc?.ToUniversalTime() ?? DateTimeOffset.UtcNow;
        Status = ImportStatus.Failed;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ImportBatch"/> class.
    /// </summary>
    public ImportBatch()
    {
        StartedAtUtc = DateTimeOffset.UtcNow;
    }

    private ImportBatch(Guid id, DateTimeOffset createdAtUtc)
        : base(id, createdAtUtc)
    {
    }
}
