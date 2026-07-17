using System.Text.Json;
using System.Text.Json.Serialization;

namespace McpTrackTokens.Application.DTOs;

/// <summary>
/// Inbound activity event from an editor hook or extension.
/// </summary>
public sealed record IngestEventDto
{
    public string SchemaVersion { get; init; } = "1.0";

    public string? ExternalEventId { get; init; }

    public string EventType { get; init; } = string.Empty;

    public DateTimeOffset TimestampUtc { get; init; }

    public string Editor { get; init; } = string.Empty;

    public string? EditorVersion { get; init; }

    public string? MachineName { get; init; }

    public string? UserName { get; init; }

    public string? ExternalSessionId { get; init; }

    public string? ExternalConversationId { get; init; }

    public string? ExternalRequestId { get; init; }

    public string? WorkspacePath { get; init; }

    public string? RepositoryPath { get; init; }

    public string? RemoteUrl { get; init; }

    public string? Branch { get; init; }

    public string? ActiveFilePath { get; init; }

    public Guid? ProjectId { get; init; }

    public string? Model { get; init; }

    public string? Provider { get; init; }

    public int? PromptLength { get; init; }

    public string? PromptHash { get; init; }

    /// <summary>
    /// Optional prompt content. Stored only when explicitly enabled in options.
    /// </summary>
    public string? PromptContent { get; init; }

    /// <summary>
    /// Optional response content. Stored only when explicitly enabled in options.
    /// </summary>
    public string? ResponseContent { get; init; }

    public string? Status { get; init; }

    public long? DurationMilliseconds { get; init; }

    public DateTimeOffset? ResponseCompletedAtUtc { get; init; }

    public JsonElement? Metadata { get; init; }
}

/// <summary>
/// Batch ingestion request.
/// </summary>
public sealed record BatchIngestRequestDto
{
    public IReadOnlyList<IngestEventDto> Events { get; init; } = [];
}

/// <summary>
/// Result of ingesting a single event.
/// </summary>
public sealed record IngestEventResultDto
{
    public Guid EventId { get; init; }

    public bool WasDuplicate { get; init; }

    public Guid? ProjectId { get; init; }

    public Guid? SessionId { get; init; }
}

/// <summary>
/// Result of batch ingestion.
/// </summary>
public sealed record BatchIngestResultDto
{
    public int Accepted { get; init; }

    public int Duplicates { get; init; }

    public int Failed { get; init; }

    public IReadOnlyList<IngestEventResultDto> Results { get; init; } = [];
}

/// <summary>
/// Session start request.
/// </summary>
public sealed record SessionStartDto
{
    public Guid? ProjectId { get; init; }

    public string Editor { get; init; } = string.Empty;

    public string? EditorVersion { get; init; }

    public string? MachineName { get; init; }

    public string? UserName { get; init; }

    public string? WorkspacePath { get; init; }

    public string? RepositoryPath { get; init; }

    public string? RemoteUrl { get; init; }

    public string? Branch { get; init; }

    public string? ExternalSessionId { get; init; }

    public string? Notes { get; init; }

    public DateTimeOffset? StartedAtUtc { get; init; }
}

/// <summary>
/// Session end request.
/// </summary>
public sealed record SessionEndDto
{
    public Guid? SessionId { get; init; }

    public string? ExternalSessionId { get; init; }

    public string? Editor { get; init; }

    public string? Notes { get; init; }

    public DateTimeOffset? EndedAtUtc { get; init; }
}

/// <summary>
/// Session heartbeat request.
/// </summary>
public sealed record HeartbeatDto
{
    public Guid? SessionId { get; init; }

    public string? ExternalSessionId { get; init; }

    public string? Editor { get; init; }

    public string? WorkspacePath { get; init; }

    public string? RepositoryPath { get; init; }

    public string? Branch { get; init; }

    public DateTimeOffset? TimestampUtc { get; init; }
}
