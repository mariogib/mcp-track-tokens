namespace McpTrackTokens.Application.DTOs;

/// <summary>
/// Result of replaying offline queued hook events into the ingest API path.
/// </summary>
public sealed record OfflineQueueReplayResultDto
{
    public int Attempted { get; init; }

    public int Flushed { get; init; }

    public int Remaining { get; init; }

    public int Failed { get; init; }

    public IReadOnlyList<string> Errors { get; init; } = [];
}
