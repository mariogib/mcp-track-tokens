using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using McpTrackTokens.Application.DTOs;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Application.Options;

namespace McpTrackTokens.Infrastructure.Persistence;

/// <summary>
/// SQLite online backup / restore for the tracking database.
/// </summary>
public sealed class DatabaseBackupService : IDatabaseBackupService
{
    private const string BackupFilePrefix = "mcp-track-tokens-backup-";
    private static readonly byte[] SqliteHeader = "SQLite format 3\0"u8.ToArray();

    private readonly TrackingOptions _options;

    public DatabaseBackupService(IOptions<TrackingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    public DatabaseBackupInfoDto GetInfo(string? destinationDirectory = null)
    {
        var defaultFolder = GetDefaultBackupFolder();
        var folder = ResolveDestinationDirectory(destinationDirectory);
        var provider = _options.DatabaseProvider?.Trim() ?? "Sqlite";
        var supports = IsSqliteProvider(provider);
        var databasePath = supports ? _options.GetResolvedDatabasePath() : string.Empty;

        return new DatabaseBackupInfoDto
        {
            DatabasePath = databasePath,
            DatabaseProvider = provider,
            SupportsBackup = supports,
            DefaultFolder = defaultFolder,
            DestinationFolder = folder,
            Backups = supports ? ListBackups(folder) : []
        };
    }

    public async Task<DatabaseBackupResultDto> BackupAsync(
        string? destinationDirectory = null,
        CancellationToken cancellationToken = default)
    {
        EnsureSqlite();
        var livePath = _options.GetResolvedDatabasePath();
        if (!File.Exists(livePath))
        {
            throw new InvalidOperationException($"Database file not found: {livePath}");
        }

        var folder = ResolveDestinationDirectory(destinationDirectory);
        Directory.CreateDirectory(folder);

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var fileName = $"{BackupFilePrefix}{stamp}.db";
        var backupPath = Path.Combine(folder, fileName);
        if (File.Exists(backupPath))
        {
            backupPath = Path.Combine(folder, $"{BackupFilePrefix}{stamp}-{Guid.NewGuid():N}.db");
        }

        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            CopySqliteDatabase(livePath, backupPath);
        }, cancellationToken).ConfigureAwait(false);

        var info = new FileInfo(backupPath);
        return new DatabaseBackupResultDto
        {
            FilePath = backupPath,
            SizeBytes = info.Length,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Message = $"Backup saved to {backupPath}"
        };
    }

    public async Task<(Stream Stream, string FileName)> CreateDownloadableBackupAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureSqlite();
        var livePath = _options.GetResolvedDatabasePath();
        if (!File.Exists(livePath))
        {
            throw new InvalidOperationException($"Database file not found: {livePath}");
        }

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var fileName = $"{BackupFilePrefix}{stamp}.db";
        var tempPath = Path.Combine(Path.GetTempPath(), $"{BackupFilePrefix}{stamp}-{Guid.NewGuid():N}.db");

        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            CopySqliteDatabase(livePath, tempPath);
        }, cancellationToken).ConfigureAwait(false);

        var stream = new FileStream(
            tempPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            options: FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        return (stream, fileName);
    }

    public async Task<DatabaseRestoreResultDto> RestoreAsync(
        string sourceFilePath,
        CancellationToken cancellationToken = default)
    {
        EnsureSqlite();
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);

        var sourcePath = TrackingOptions.ExpandPath(sourceFilePath.Trim());
        if (!Path.IsPathRooted(sourcePath))
        {
            throw new InvalidOperationException("Restore path must be an absolute path.");
        }

        sourcePath = Path.GetFullPath(sourcePath);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Backup file not found.", sourcePath);
        }

        ValidateSqliteFile(sourcePath);

        var livePath = _options.GetResolvedDatabasePath();
        string? safetyPath = null;
        if (File.Exists(livePath))
        {
            var safety = await BackupAsync(GetDefaultBackupFolder(), cancellationToken).ConfigureAwait(false);
            safetyPath = safety.FilePath;
        }

        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            SqliteConnection.ClearAllPools();
            CopySqliteDatabase(sourcePath, livePath);
            DeleteSidecarFiles(livePath);
            SqliteConnection.ClearAllPools();
        }, cancellationToken).ConfigureAwait(false);

        return new DatabaseRestoreResultDto
        {
            RestoredFromPath = sourcePath,
            SafetyBackupPath = safetyPath,
            RestartRecommended = true,
            Message = safetyPath is null
                ? $"Database restored from {sourcePath}. Restart the tracking host to reload cleanly."
                : $"Database restored from {sourcePath}. A safety backup of the previous database was saved to {safetyPath}. Restart the tracking host to reload cleanly."
        };
    }

    private void EnsureSqlite()
    {
        if (!IsSqliteProvider(_options.DatabaseProvider))
        {
            throw new InvalidOperationException(
                "Backup and restore are only supported for the Sqlite database provider.");
        }
    }

    private static bool IsSqliteProvider(string? provider)
    {
        var value = provider?.Trim() ?? "Sqlite";
        return value.Equals("Sqlite", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("SQLite", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetDefaultBackupFolder()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var root = !string.IsNullOrWhiteSpace(documents)
            ? documents
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var folder = Path.GetFullPath(Path.Combine(root, "MCP Track Tokens"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static string ResolveDestinationDirectory(string? destinationDirectory)
    {
        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            return GetDefaultBackupFolder();
        }

        var expanded = TrackingOptions.ExpandPath(destinationDirectory.Trim());
        if (!Path.IsPathRooted(expanded))
        {
            throw new InvalidOperationException("Backup folder must be an absolute path.");
        }

        var folder = Path.GetFullPath(expanded);
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static IReadOnlyList<DatabaseBackupFileDto> ListBackups(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }

        return Directory.EnumerateFiles(folder, $"{BackupFilePrefix}*.db")
            .Select(path =>
            {
                var info = new FileInfo(path);
                return new DatabaseBackupFileDto
                {
                    FileName = info.Name,
                    FullPath = info.FullName,
                    SizeBytes = info.Length,
                    CreatedAtUtc = info.CreationTimeUtc
                };
            })
            .OrderByDescending(b => b.CreatedAtUtc)
            .Take(50)
            .ToList();
    }

    private static void CopySqliteDatabase(string sourcePath, string destinationPath)
    {
        var destinationDir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(destinationDir))
        {
            Directory.CreateDirectory(destinationDir);
        }

        if (File.Exists(destinationPath))
        {
            File.Delete(destinationPath);
        }

        DeleteSidecarFiles(destinationPath);

        using var source = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = sourcePath,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString());
        source.Open();

        using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = destinationPath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString());
        destination.Open();

        source.BackupDatabase(destination);
    }

    private static void DeleteSidecarFiles(string databasePath)
    {
        foreach (var sidecar in new[] { databasePath + "-wal", databasePath + "-shm" })
        {
            if (File.Exists(sidecar))
            {
                File.Delete(sidecar);
            }
        }
    }

    private static void ValidateSqliteFile(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[SqliteHeader.Length];
        var read = stream.Read(header);
        if (read < SqliteHeader.Length || !header.SequenceEqual(SqliteHeader))
        {
            throw new InvalidOperationException("The selected file is not a valid SQLite database.");
        }
    }
}
