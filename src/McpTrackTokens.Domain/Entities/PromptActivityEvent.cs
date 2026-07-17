using McpTrackTokens.Domain.Common;
using McpTrackTokens.Domain.Enums;

namespace McpTrackTokens.Domain.Entities;

/// <summary>
/// A discrete activity event emitted by an editor, hook, or agent.
/// </summary>
public sealed class PromptActivityEvent : EntityBase
{
    /// <summary>
    /// Gets or sets the associated project identifier, when known.
    /// </summary>
    public Guid? ProjectId { get; set; }

    /// <summary>
    /// Gets or sets the editor session identifier, when known.
    /// </summary>
    public Guid? EditorSessionId { get; set; }

    /// <summary>
    /// Gets or sets the activity event type.
    /// </summary>
    public ActivityEventType EventType { get; set; }

    /// <summary>
    /// Gets or sets the editor that produced the event.
    /// </summary>
    public EditorType Editor { get; set; }

    /// <summary>
    /// Gets or sets the event timestamp in UTC.
    /// </summary>
    public DateTimeOffset TimestampUtc { get; set; }

    /// <summary>
    /// Gets or sets the external event identifier used for idempotency.
    /// </summary>
    public string? ExternalEventId { get; set; }

    /// <summary>
    /// Gets or sets the external conversation identifier.
    /// </summary>
    public string? ExternalConversationId { get; set; }

    /// <summary>
    /// Gets or sets the external request identifier.
    /// </summary>
    public string? ExternalRequestId { get; set; }

    /// <summary>
    /// Gets or sets the workspace path.
    /// </summary>
    public string? WorkspacePath { get; set; }

    /// <summary>
    /// Gets or sets the repository path.
    /// </summary>
    public string? RepositoryPath { get; set; }

    /// <summary>
    /// Gets or sets the remote Git URL.
    /// </summary>
    public string? RemoteUrl { get; set; }

    /// <summary>
    /// Gets or sets the Git branch.
    /// </summary>
    public string? Branch { get; set; }

    /// <summary>
    /// Gets or sets the prompt length in characters, when available.
    /// </summary>
    public int? PromptLength { get; set; }

    /// <summary>
    /// Gets or sets the salted SHA-256 prompt hash, when hashing is enabled.
    /// </summary>
    public string? PromptHash { get; set; }

    /// <summary>
    /// Gets or sets whether prompt content was stored.
    /// Defaults to <c>false</c> for privacy.
    /// </summary>
    public bool PromptContentStored { get; set; }

    /// <summary>
    /// Gets or sets encrypted prompt content when storage is explicitly enabled.
    /// </summary>
    public string? PromptContentEncrypted { get; set; }

    /// <summary>
    /// Gets or sets when the response completed in UTC, if applicable.
    /// </summary>
    public DateTimeOffset? ResponseCompletedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the duration in milliseconds, if known.
    /// </summary>
    public long? DurationMilliseconds { get; set; }

    /// <summary>
    /// Gets or sets the model name.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Gets or sets the AI provider.
    /// </summary>
    public AIProvider? Provider { get; set; }

    /// <summary>
    /// Gets or sets the activity status.
    /// </summary>
    public ActivityStatus Status { get; set; } = ActivityStatus.Unknown;

    /// <summary>
    /// Gets or sets the attribution method applied to this event.
    /// </summary>
    public AttributionMethod AttributionMethod { get; set; } = AttributionMethod.Unallocated;

    /// <summary>
    /// Gets or sets the attribution confidence.
    /// </summary>
    public AttributionConfidence AttributionConfidence { get; set; } = AttributionConfidence.Unallocated;

    /// <summary>
    /// Gets or sets optional metadata as JSON.
    /// </summary>
    public string? MetadataJson { get; set; }

    /// <summary>
    /// Creates a prompt activity event with privacy-safe defaults.
    /// </summary>
    public static PromptActivityEvent Create(
        ActivityEventType eventType,
        EditorType editor,
        DateTimeOffset timestampUtc,
        Guid? projectId = null,
        Guid? editorSessionId = null,
        string? externalEventId = null,
        string? externalConversationId = null,
        string? externalRequestId = null,
        string? workspacePath = null,
        string? repositoryPath = null,
        string? remoteUrl = null,
        string? branch = null,
        int? promptLength = null,
        string? promptHash = null,
        bool promptContentStored = false,
        string? promptContentEncrypted = null,
        DateTimeOffset? responseCompletedAtUtc = null,
        long? durationMilliseconds = null,
        string? model = null,
        AIProvider? provider = null,
        ActivityStatus status = ActivityStatus.Unknown,
        AttributionMethod attributionMethod = AttributionMethod.Unallocated,
        AttributionConfidence attributionConfidence = AttributionConfidence.Unallocated,
        string? metadataJson = null,
        Guid? id = null,
        DateTimeOffset? createdAtUtc = null)
    {
        if (promptLength is not null)
        {
            Guard.AgainstNegative(promptLength.Value);
        }

        if (durationMilliseconds is not null)
        {
            Guard.AgainstNegative(durationMilliseconds.Value);
        }

        if (!promptContentStored)
        {
            promptContentEncrypted = null;
        }

        var created = createdAtUtc?.ToUniversalTime() ?? DateTimeOffset.UtcNow;
        return new PromptActivityEvent(id ?? Guid.NewGuid(), created)
        {
            ProjectId = projectId,
            EditorSessionId = editorSessionId,
            EventType = eventType,
            Editor = editor,
            TimestampUtc = timestampUtc.ToUniversalTime(),
            ExternalEventId = externalEventId,
            ExternalConversationId = externalConversationId,
            ExternalRequestId = externalRequestId,
            WorkspacePath = workspacePath,
            RepositoryPath = repositoryPath,
            RemoteUrl = remoteUrl,
            Branch = branch,
            PromptLength = promptLength,
            PromptHash = promptHash,
            PromptContentStored = promptContentStored,
            PromptContentEncrypted = promptContentEncrypted,
            ResponseCompletedAtUtc = responseCompletedAtUtc?.ToUniversalTime(),
            DurationMilliseconds = durationMilliseconds,
            Model = model,
            Provider = provider,
            Status = status,
            AttributionMethod = attributionMethod,
            AttributionConfidence = attributionConfidence,
            MetadataJson = metadataJson
        };
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PromptActivityEvent"/> class.
    /// </summary>
    public PromptActivityEvent()
    {
        TimestampUtc = DateTimeOffset.UtcNow;
    }

    private PromptActivityEvent(Guid id, DateTimeOffset createdAtUtc)
        : base(id, createdAtUtc)
    {
    }
}
