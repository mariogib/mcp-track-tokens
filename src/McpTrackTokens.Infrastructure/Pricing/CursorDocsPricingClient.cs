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
    private static readonly string[] CandidateMarkdownUrls =
    [
        CursorDocsPricingMarkdownParser.DocsMarkdownUrl,
        "https://www.cursor.com/docs/models-and-pricing.md",
    ];

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
        string? markdown = null;
        string? lastError = null;

        foreach (var url in CandidateMarkdownUrls)
        {
            try
            {
                markdown = await DownloadMarkdownAsync(url, useBrowserUserAgent: false, cancellationToken)
                    .ConfigureAwait(false);
                if (LooksLikePricingDocs(markdown))
                {
                    break;
                }

                lastError = $"Response from {url} did not contain a recognizable Model pricing section.";
                markdown = null;
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
            {
                lastError = ex.Message;
                _logger.LogWarning(ex, "Failed to download Cursor pricing docs from {Url}.", url);
            }
        }

        if (markdown is null)
        {
            // Some edges block non-browser user agents with a 404 from the docs CDN.
            try
            {
                markdown = await DownloadMarkdownAsync(
                        CursorDocsPricingMarkdownParser.DocsMarkdownUrl,
                        useBrowserUserAgent: true,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!LooksLikePricingDocs(markdown))
                {
                    markdown = null;
                    lastError = "Cursor pricing docs response did not contain a recognizable Model pricing section.";
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
            {
                lastError = ex.Message;
                _logger.LogWarning(ex, "Browser-UA fallback failed for Cursor pricing docs.");
            }
        }

        if (markdown is null)
        {
            throw new InvalidOperationException(
                lastError ?? "Failed to download Cursor pricing docs.");
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

    private async Task<string> DownloadMarkdownAsync(
        string url,
        bool useBrowserUserAgent,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Clear();
        if (useBrowserUserAgent)
        {
            request.Headers.TryAddWithoutValidation(
                "User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36");
        }
        else
        {
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("mcp-track-tokens", "1.0"));
        }

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/markdown"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Failed to download Cursor pricing docs ({(int)response.StatusCode} {response.ReasonPhrase}).");
        }

        return body;
    }

    private static bool LooksLikePricingDocs(string? markdown) =>
        !string.IsNullOrWhiteSpace(markdown) &&
        markdown.Contains("Model pricing", StringComparison.OrdinalIgnoreCase);
}
