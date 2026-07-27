using McpTrackTokens.Application.Options;

namespace McpTrackTokens.Application.Interfaces;

/// <summary>
/// Loads and persists user-editable tracking settings in the tracking database.
/// </summary>
public interface ITrackingSettingsStore
{
    /// <summary>
    /// Overlays persisted settings onto <paramref name="options"/> when a row exists.
    /// </summary>
    Task LoadIntoAsync(TrackingOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists the editable settings from <paramref name="options"/> to the database.
    /// </summary>
    Task SaveAsync(TrackingOptions options, CancellationToken cancellationToken = default);
}
