namespace McpTrackTokens.Application.DTOs;

/// <summary>
/// Default backup folder and existing backup files.
/// </summary>
public sealed record DatabaseBackupInfoDto
{
    public string DatabasePath { get; init; } = string.Empty;

    public string DatabaseProvider { get; init; } = string.Empty;

    public bool SupportsBackup { get; init; }

    /// <summary>
    /// Default folder for backups (My Documents).
    /// </summary>
    public string DefaultFolder { get; init; } = string.Empty;

    public string DestinationFolder { get; init; } = string.Empty;

    public IReadOnlyList<DatabaseBackupFileDto> Backups { get; init; } = [];
}

/// <summary>
/// A backup file on disk.
/// </summary>
public sealed record DatabaseBackupFileDto
{
    public string FileName { get; init; } = string.Empty;

    public string FullPath { get; init; } = string.Empty;

    public long SizeBytes { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }
}

/// <summary>
/// Request body for creating a backup.
/// </summary>
public sealed record DatabaseBackupRequestDto
{
    /// <summary>
    /// Destination folder. When null/empty, My Documents is used.
    /// </summary>
    public string? DestinationDirectory { get; init; }
}

/// <summary>
/// Result of a successful backup.
/// </summary>
public sealed record DatabaseBackupResultDto
{
    public string FilePath { get; init; } = string.Empty;

    public long SizeBytes { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Request body for restoring from a path on the server host.
/// </summary>
public sealed record DatabaseRestoreRequestDto
{
    public string SourceFilePath { get; init; } = string.Empty;
}

/// <summary>
/// Result of a restore operation.
/// </summary>
public sealed record DatabaseRestoreResultDto
{
    public string RestoredFromPath { get; init; } = string.Empty;

    public string? SafetyBackupPath { get; init; }

    public bool RestartRecommended { get; init; } = true;

    public string Message { get; init; } = string.Empty;
}
