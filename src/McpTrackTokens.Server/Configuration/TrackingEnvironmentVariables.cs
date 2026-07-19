using McpTrackTokens.Application.Options;

namespace McpTrackTokens.Server.Configuration;

/// <summary>
/// Applies <c>MCP_TRACK_TOKENS_*</c> environment variables onto the Tracking configuration section.
/// </summary>
public static class TrackingEnvironmentVariables
{
    private static readonly (string EnvSuffix, string ConfigKey)[] Mappings =
    [
        ("DATABASE_PROVIDER", "DatabaseProvider"),
        ("DATABASE_PATH", "DatabasePath"),
        ("CONNECTION_STRING", "ConnectionString"),
        ("SERVER_URL", "ServerUrl"),
        ("BIND_ADDRESS", "BindAddress"),
        ("API_KEY", "ApiKey"),
        ("EXPORT_PATH", "ExportPath"),
        ("LOG_PATH", "LogPath"),
        ("LOG_LEVEL", "LogLevel"),
        ("DEFAULT_CURRENCY", "DefaultCurrency"),
        ("INACTIVITY_THRESHOLD_MINUTES", "InactivityThresholdMinutes"),
        ("ENABLE_HTTP_MCP", "EnableHttpMcp"),
        ("AUTO_CREATE_PROJECTS", "AutoCreateProjects"),
        ("STORE_PROMPT_CONTENT", "StorePromptContent"),
        ("STORE_RESPONSE_CONTENT", "StoreResponseContent"),
        ("ENABLE_PROMPT_HASHING", "EnablePromptHashing"),
        ("MAX_METADATA_BYTES", "MaxMetadataBytes"),
        ("CURSOR_SUBSCRIPTION_AMOUNT", "CursorSubscriptionAmount"),
        ("CURSOR_SUBSCRIPTION_CURRENCY", "CursorSubscriptionCurrency"),
        ("CURSOR_ALLOCATION_METHOD", "CursorAllocationMethod"),
        ("ENCRYPTION_KEY_PATH", "EncryptionKeyPath"),
        ("QUEUE_PATH", "QueuePath"),
        ("MAX_QUEUED_EVENTS", "MaxQueuedEvents"),
        ("MAX_REQUEST_BYTES", "MaxRequestBytes"),
        ("MAX_BACKUP_UPLOAD_BYTES", "MaxBackupUploadBytes"),
        ("MIGRATE_ON_STARTUP", "MigrateOnStartup")
    ];

    /// <summary>
    /// Copies known environment overrides into <c>Tracking:*</c> configuration keys.
    /// </summary>
    public static void Apply(IConfigurationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (envSuffix, configKey) in Mappings)
        {
            var value = Environment.GetEnvironmentVariable(TrackingOptions.EnvironmentPrefix + envSuffix);
            if (!string.IsNullOrWhiteSpace(value))
            {
                data[$"{TrackingOptions.SectionName}:{configKey}"] = value;
            }
        }

        if (data.Count > 0)
        {
            builder.AddInMemoryCollection(data);
        }
    }

    /// <summary>
    /// Applies CLI / process argument flags that override configuration.
    /// </summary>
    public static void ApplyArgs(IConfigurationBuilder builder, bool migrate)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (!migrate)
        {
            return;
        }

        builder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{TrackingOptions.SectionName}:MigrateOnStartup"] = "true"
        });
    }
}
