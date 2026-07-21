using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using McpTrackTokens.Application.DTOs;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Application.Services;

namespace McpTrackTokens.Infrastructure.Pricing;

/// <summary>
/// Downloads Cursor docs markdown and parses model pricing into a rate card.
/// </summary>
public sealed class CursorDocsPricingClient : ICursorDocsPricingClient
{
    private readonly HttpClient _http;
    private readonly ILogger<CursorDocsPricingClient> _logger;

    public CursorDocsPricingClient(HttpClient http, ILogger<CursorDocsPricingClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<CursorDocsPricingFetchResult> FetchRatesAsync(
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            CursorDocsPricingMarkdownParser.DocsMarkdownUrl);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("mcp-track-tokens", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/markdown"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var markdown = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Failed to download Cursor pricing docs ({(int)response.StatusCode} {response.ReasonPhrase}).");
        }

        if (string.IsNullOrWhiteSpace(markdown) ||
            !markdown.Contains("Model pricing", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Cursor pricing docs response did not contain a recognizable Model pricing section.");
        }

        var (rates, warnings) = CursorDocsPricingMarkdownParser.ParseWithWarnings(markdown);
        if (rates.Count == 0)
        {
            throw new InvalidOperationException("Parsed Cursor pricing docs but found no model rates.");
        }

        _logger.LogInformation(
            "Fetched {Count} Cursor token rates from docs ({Warnings} warnings).",
            rates.Count,
            warnings.Count);

        return new CursorDocsPricingFetchResult
        {
            SourceUrl = CursorDocsPricingMarkdownParser.DocsPageUrl,
            FetchedAtUtc = DateTimeOffset.UtcNow,
            Count = rates.Count,
            Warnings = warnings,
            Rates = rates
        };
    }
}
