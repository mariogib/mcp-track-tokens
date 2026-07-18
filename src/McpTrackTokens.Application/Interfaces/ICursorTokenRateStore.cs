using McpTrackTokens.Application.Options;

namespace McpTrackTokens.Application.Interfaces;

/// <summary>
/// Loads and persists Cursor token rate cards beside the tracking database.
/// </summary>
public interface ICursorTokenRateStore
{
    /// <summary>
    /// Loads persisted rates into <paramref name="options"/> when a file exists.
    /// </summary>
    Task LoadIntoAsync(TrackingOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists the current rate card from <paramref name="options"/>.
    /// </summary>
    Task SaveAsync(TrackingOptions options, CancellationToken cancellationToken = default);
}
