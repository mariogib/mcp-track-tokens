namespace McpTrackTokens.Application.DTOs;

/// <summary>
/// Result of checking whether Cursor hooks are installed and use event names
/// compatible with the installed Cursor version.
/// </summary>
public sealed record CursorHooksCompatibilityReportDto
{
    /// <summary>Overall verdict: compatible, degraded, or incompatible.</summary>
    public required string Status { get; init; }

    /// <summary>Short human-readable summary.</summary>
    public required string Summary { get; init; }

    /// <summary>Installed Cursor version when detected.</summary>
    public string? CursorVersion { get; init; }

    /// <summary>How <see cref="CursorVersion"/> was determined.</summary>
    public string? CursorVersionSource { get; init; }

    /// <summary>Path to <c>~/.cursor</c> (or override) used for the check.</summary>
    public required string CursorUserDirectory { get; init; }

    /// <summary>Path to installed hook scripts directory, if present.</summary>
    public string? HooksInstallDirectory { get; init; }

    /// <summary>Path to <c>hooks.json</c> when found.</summary>
    public string? HooksConfigPath { get; init; }

    /// <summary>Top-level <c>version</c> from hooks.json when present.</summary>
    public int? HooksConfigSchemaVersion { get; init; }

    /// <summary>Individual check results.</summary>
    public IReadOnlyList<CursorHooksCompatibilityCheckDto> Checks { get; init; } = [];

    /// <summary>Cursor hook events wired to MCP Track Tokens scripts.</summary>
    public IReadOnlyList<string> WiredEvents { get; init; } = [];

    /// <summary>Legacy event names that Cursor no longer recognizes.</summary>
    public IReadOnlyList<string> LegacyEvents { get; init; } = [];

    /// <summary>Recommended remediation steps.</summary>
    public IReadOnlyList<string> Recommendations { get; init; } = [];

    /// <summary>Most recent Cursor activity event ingested by the tracker, if any.</summary>
    public DateTimeOffset? LastCursorEventAtUtc { get; init; }

    /// <summary><c>editorVersion</c> from the most recent Cursor activity event, if any.</summary>
    public string? LastCursorEventEditorVersion { get; init; }

    /// <summary>Id of the probe event ingested by this check, when successful.</summary>
    public Guid? ProbeEventId { get; init; }

    /// <summary>When the probe event was ingested.</summary>
    public DateTimeOffset? ProbeIngestedAtUtc { get; init; }
}

/// <summary>
/// One discrete compatibility check.
/// </summary>
public sealed record CursorHooksCompatibilityCheckDto
{
    public required string Id { get; init; }

    public required string Status { get; init; }

    public required string Message { get; init; }
}
