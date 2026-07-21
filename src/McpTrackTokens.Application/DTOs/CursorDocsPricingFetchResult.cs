using McpTrackTokens.Application.Options;

namespace McpTrackTokens.Application.DTOs;

/// <summary>
/// Result of fetching Cursor Models &amp; Pricing into a local rate card.
/// </summary>
public sealed record CursorDocsPricingFetchResult
{
    public string SourceUrl { get; init; } = string.Empty;

    public DateTimeOffset FetchedAtUtc { get; init; }

    public int Count { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public IReadOnlyList<CursorModelTokenRate> Rates { get; init; } = [];
}
