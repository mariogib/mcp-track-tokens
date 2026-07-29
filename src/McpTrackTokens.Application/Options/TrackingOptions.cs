using McpTrackTokens.Domain.Enums;

namespace McpTrackTokens.Application.Options;

/// <summary>
/// Strongly typed configuration for MCP Track Tokens.
/// Bound from the <c>Tracking</c> section or <c>MCP_TRACK_TOKENS_*</c> environment variables.
/// </summary>
public sealed class TrackingOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "Tracking";

    /// <summary>
    /// Environment variable prefix.
    /// </summary>
    public const string EnvironmentPrefix = "MCP_TRACK_TOKENS_";

    /// <summary>
    /// Database provider name (<c>Sqlite</c> or <c>PostgreSQL</c>).
    /// </summary>
    public string DatabaseProvider { get; set; } = "Sqlite";

    /// <summary>
    /// SQLite database file path. Supports <c>~</c> for the user profile.
    /// </summary>
    public string DatabasePath { get; set; } = "~/.mcp-track-tokens/mcp-track-tokens.db";

    /// <summary>
    /// Optional connection string (used for PostgreSQL).
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Public server URL used by clients and integrations.
    /// </summary>
    public string ServerUrl { get; set; } = "http://127.0.0.1:5187";

    /// <summary>
    /// HTTP bind address for the local ingestion API.
    /// </summary>
    public string BindAddress { get; set; } = "http://127.0.0.1:5187";

    /// <summary>
    /// Optional bootstrap API key (plaintext; hashed before persistence).
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Default export directory. Supports <c>~</c>.
    /// </summary>
    public string ExportPath { get; set; } = "~/.mcp-track-tokens/exports/";

    /// <summary>
    /// Additional approved export directories (absolute or <c>~</c>-prefixed).
    /// </summary>
    public List<string> ApprovedExportDirectories { get; set; } = [];

    /// <summary>
    /// Log file directory or path. Supports <c>~</c>.
    /// </summary>
    public string LogPath { get; set; } = "~/.mcp-track-tokens/logs/";

    /// <summary>
    /// Minimum log level name.
    /// </summary>
    public string LogLevel { get; set; } = "Information";

    /// <summary>
    /// Default ISO currency for projects and subscription allocation.
    /// </summary>
    public string DefaultCurrency { get; set; } = "USD";

    /// <summary>
    /// Inactivity threshold used when calculating activity windows.
    /// </summary>
    public int InactivityThresholdMinutes { get; set; } = 15;

    /// <summary>
    /// When an active editor session has had no prompt for longer than this many minutes,
    /// the next prompt closes that session (at the last prompt time) and opens a new one.
    /// </summary>
    public int SessionInactivityCloseMinutes { get; set; } = 60;

    /// <summary>
    /// Enables optional streamable HTTP MCP transport.
    /// </summary>
    public bool EnableHttpMcp { get; set; }

    /// <summary>
    /// When true, unknown repositories may auto-create projects.
    /// </summary>
    public bool AutoCreateProjects { get; set; } = true;

    /// <summary>
    /// When true, prompt content may be stored encrypted at rest.
    /// </summary>
    public bool StorePromptContent { get; set; }

    /// <summary>
    /// When true, response content may be stored encrypted at rest.
    /// </summary>
    public bool StoreResponseContent { get; set; }

    /// <summary>
    /// When true, salted prompt hashes are computed for duplicate detection.
    /// </summary>
    public bool EnablePromptHashing { get; set; } = true;

    /// <summary>
    /// Maximum serialized metadata payload size in bytes.
    /// </summary>
    public int MaxMetadataBytes { get; set; } = 16384;

    /// <summary>
    /// Fixed monthly Cursor subscription amount (separate from usage-based cost).
    /// </summary>
    public decimal CursorSubscriptionAmount { get; set; }

    /// <summary>
    /// Currency for the Cursor subscription amount.
    /// </summary>
    public string CursorSubscriptionCurrency { get; set; } = "USD";

    /// <summary>
    /// Default subscription allocation method.
    /// </summary>
    public AllocationRuleType CursorAllocationMethod { get; set; } = AllocationRuleType.NotAllocated;

    /// <summary>
    /// Cursor model token rates ($ per 1M tokens) used for cost estimates.
    /// </summary>
    public List<CursorModelTokenRate> CursorTokenRates { get; set; } = [];

    /// <summary>
    /// When true, estimate usage cost from <see cref="CursorTokenRates"/> when imported cost is zero.
    /// </summary>
    public bool EstimateCostFromTokenRates { get; set; }

    /// <summary>
    /// Optional content-encryption key path (outside the database). Supports <c>~</c>.
    /// </summary>
    public string EncryptionKeyPath { get; set; } = "~/.mcp-track-tokens/encryption.key";

    /// <summary>
    /// Local offline event queue directory. Supports <c>~</c>.
    /// </summary>
    public string QueuePath { get; set; } = "~/.mcp-track-tokens/queue/";

    /// <summary>
    /// Maximum queued events retained on disk.
    /// </summary>
    public int MaxQueuedEvents { get; set; } = 10_000;

    /// <summary>
    /// HTTP ingestion request body size limit in bytes.
    /// </summary>
    public int MaxRequestBytes { get; set; } = 1_048_576;

    /// <summary>
    /// Maximum multipart upload size for database restore (bytes). Default 100 MiB.
    /// </summary>
    public long MaxBackupUploadBytes { get; set; } = 104_857_600;

    /// <summary>
    /// When true, EF Core migrations are applied on host startup.
    /// </summary>
    public bool MigrateOnStartup { get; set; }

    /// <summary>
    /// Optional retention window in days for activity/usage cleanup jobs. Null means retain indefinitely.
    /// </summary>
    public int? DataRetentionDays { get; set; }

    /// <summary>
    /// Expands <c>~</c> to the user profile directory and normalizes directory separators.
    /// </summary>
    public static string ExpandPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var trimmed = path.Trim();
        if (trimmed.StartsWith("~/") || trimmed.StartsWith("~\\"))
        {
            trimmed = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                trimmed[2..]);
        }
        else if (trimmed == "~")
        {
            trimmed = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        return Path.GetFullPath(trimmed);
    }

    /// <summary>
    /// Returns the resolved database path.
    /// </summary>
    public string GetResolvedDatabasePath() => ExpandPath(DatabasePath);

    /// <summary>
    /// Returns the resolved export directory path (always ends with a separator).
    /// </summary>
    public string GetResolvedExportPath()
    {
        var resolved = ExpandPath(ExportPath);
        return resolved.EndsWith(Path.DirectorySeparatorChar) || resolved.EndsWith(Path.AltDirectorySeparatorChar)
            ? resolved
            : resolved + Path.DirectorySeparatorChar;
    }

    /// <summary>
    /// Returns approved export roots including the default export path.
    /// </summary>
    public IReadOnlyList<string> GetApprovedExportRoots()
    {
        var roots = new List<string> { GetResolvedExportPath() };
        foreach (var directory in ApprovedExportDirectories)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            var resolved = ExpandPath(directory);
            if (!resolved.EndsWith(Path.DirectorySeparatorChar) &&
                !resolved.EndsWith(Path.AltDirectorySeparatorChar))
            {
                resolved += Path.DirectorySeparatorChar;
            }

            if (!roots.Contains(resolved, StringComparer.OrdinalIgnoreCase))
            {
                roots.Add(resolved);
            }
        }

        return roots;
    }
}
