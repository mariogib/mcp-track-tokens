using McpTrackTokens.Application.DTOs;

namespace McpTrackTokens.Application.Interfaces;

/// <summary>
/// Fetches Cursor Models &amp; Pricing and maps it to a local token rate card.
/// </summary>
public interface ICursorDocsPricingClient
{
    /// <summary>
    /// Downloads and parses https://cursor.com/docs/models-and-pricing into rate rows.
    /// Does not persist settings — callers apply the result to a draft and save explicitly.
    /// </summary>
    Task<CursorDocsPricingFetchResult> FetchRatesAsync(CancellationToken cancellationToken = default);
}
