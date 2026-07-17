using McpTrackTokens.Domain.Common;

namespace McpTrackTokens.Domain.Entities;

/// <summary>
/// A local API key used to authenticate ingestion and write endpoints.
/// Stores only a hash of the key material — never the plaintext key.
/// </summary>
public sealed class TrackingApiKey : EntityBase
{
    /// <summary>
    /// Gets or sets the display name for the key.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the one-way hash of the API key.
    /// </summary>
    public string KeyHash { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets when the key was last used in UTC.
    /// </summary>
    public DateTimeOffset? LastUsedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets when the key expires in UTC, if applicable.
    /// </summary>
    public DateTimeOffset? ExpiresAtUtc { get; set; }

    /// <summary>
    /// Gets or sets whether the key is active.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Gets or sets a comma-separated allow-list of editors, or null for any.
    /// </summary>
    public string? AllowedEditors { get; set; }

    /// <summary>
    /// Gets or sets a comma-separated allow-list of machine names, or null for any.
    /// </summary>
    public string? AllowedMachineNames { get; set; }

    /// <summary>
    /// Creates a tracking API key entity from an already-computed key hash.
    /// </summary>
    /// <param name="name">Display name.</param>
    /// <param name="keyHash">One-way hash of the plaintext key. Never pass plaintext here.</param>
    /// <param name="expiresAtUtc">Optional expiry.</param>
    /// <param name="allowedEditors">Optional editor allow-list.</param>
    /// <param name="allowedMachineNames">Optional machine allow-list.</param>
    /// <param name="id">Optional identifier.</param>
    /// <param name="createdAtUtc">Optional creation timestamp.</param>
    public static TrackingApiKey Create(
        string name,
        string keyHash,
        DateTimeOffset? expiresAtUtc = null,
        string? allowedEditors = null,
        string? allowedMachineNames = null,
        Guid? id = null,
        DateTimeOffset? createdAtUtc = null)
    {
        return new TrackingApiKey(id ?? Guid.NewGuid(), createdAtUtc ?? DateTimeOffset.UtcNow)
        {
            Name = Guard.AgainstNullOrWhiteSpace(name).Trim(),
            KeyHash = Guard.AgainstNullOrWhiteSpace(keyHash).Trim(),
            ExpiresAtUtc = expiresAtUtc?.ToUniversalTime(),
            AllowedEditors = string.IsNullOrWhiteSpace(allowedEditors) ? null : allowedEditors.Trim(),
            AllowedMachineNames = string.IsNullOrWhiteSpace(allowedMachineNames) ? null : allowedMachineNames.Trim(),
            IsActive = true
        };
    }

    /// <summary>
    /// Returns whether the key can be used at the specified time.
    /// </summary>
    public bool IsValidAt(DateTimeOffset utcNow)
    {
        if (!IsActive)
        {
            return false;
        }

        return ExpiresAtUtc is null || ExpiresAtUtc > utcNow.ToUniversalTime();
    }

    /// <summary>
    /// Records a successful use of the key.
    /// </summary>
    public void RecordUse(DateTimeOffset? usedAtUtc = null)
    {
        LastUsedAtUtc = usedAtUtc?.ToUniversalTime() ?? DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TrackingApiKey"/> class.
    /// </summary>
    public TrackingApiKey()
    {
    }

    private TrackingApiKey(Guid id, DateTimeOffset createdAtUtc)
        : base(id, createdAtUtc)
    {
    }
}
