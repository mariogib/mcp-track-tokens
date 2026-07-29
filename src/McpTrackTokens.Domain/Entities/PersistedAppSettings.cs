using McpTrackTokens.Domain.Common;

namespace McpTrackTokens.Domain.Entities;

/// <summary>
/// Single-row document that persists dashboard tracking settings in the database.
/// </summary>
public sealed class PersistedAppSettings : EntityBase, IAuditable
{
    /// <summary>
    /// Well-known primary key for the singleton settings row.
    /// </summary>
    public static readonly Guid SingletonId = Guid.Parse("a1b2c3d4-e5f6-4789-a012-222222222201");

    /// <summary>
    /// Gets or sets the JSON payload of user-editable tracking settings.
    /// </summary>
    public string PayloadJson { get; set; } = "{}";

    /// <inheritdoc />
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>
    /// Creates the singleton settings row.
    /// </summary>
    public static PersistedAppSettings Create(string payloadJson, Guid? id = null, DateTimeOffset? atUtc = null)
    {
        var now = (atUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
        return new PersistedAppSettings(id ?? SingletonId, now)
        {
            PayloadJson = string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson,
            UpdatedAtUtc = now
        };
    }

    /// <summary>
    /// Replaces the JSON payload.
    /// </summary>
    public void ReplacePayload(string payloadJson)
    {
        PayloadJson = string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private PersistedAppSettings(Guid id, DateTimeOffset createdAtUtc)
        : base(id, createdAtUtc)
    {
    }
}
