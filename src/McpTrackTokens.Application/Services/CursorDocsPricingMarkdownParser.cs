using System.Globalization;
using System.Text.RegularExpressions;
using McpTrackTokens.Application.Options;

namespace McpTrackTokens.Application.Services;

/// <summary>
/// Parses Cursor Models &amp; Pricing markdown into a token rate card.
/// </summary>
public static partial class CursorDocsPricingMarkdownParser
{
    public const string DocsMarkdownUrl = "https://cursor.com/docs/models-and-pricing.md";
    public const string DocsPageUrl = "https://cursor.com/docs/models-and-pricing";

    /// <summary>
    /// Parses Auto pricing plus the API model pricing table from docs markdown.
    /// </summary>
    public static IReadOnlyList<CursorModelTokenRate> Parse(string markdown)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(markdown);

        var rates = new List<CursorModelTokenRate>();
        var warnings = new List<string>();

        if (TryParseAutoRates(markdown, out var auto))
        {
            rates.Add(auto);
            rates.Add(new CursorModelTokenRate
            {
                Model = "*",
                InputPerMillion = auto.InputPerMillion,
                OutputPerMillion = auto.OutputPerMillion,
                CacheReadPerMillion = auto.CacheReadPerMillion,
                CacheWritePerMillion = auto.CacheWritePerMillion
            });
        }

        rates.AddRange(ParseModelPricingTable(markdown, warnings));
        EnsureAutoFallbackRates(rates);

        return rates
            .Where(r => !string.IsNullOrWhiteSpace(r.Model))
            .GroupBy(r => r.Model, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .OrderBy(r => r.Model == "*" ? "\uFFFF" : r.Model == "Auto" ? "\u0000" : r.Model,
                StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Parses markdown and returns rates plus non-fatal warnings.
    /// </summary>
    public static (IReadOnlyList<CursorModelTokenRate> Rates, IReadOnlyList<string> Warnings) ParseWithWarnings(
        string markdown)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(markdown);

        var rates = new List<CursorModelTokenRate>();
        var warnings = new List<string>();

        if (TryParseAutoRates(markdown, out var auto))
        {
            rates.Add(auto);
            rates.Add(new CursorModelTokenRate
            {
                Model = "*",
                InputPerMillion = auto.InputPerMillion,
                OutputPerMillion = auto.OutputPerMillion,
                CacheReadPerMillion = auto.CacheReadPerMillion,
                CacheWritePerMillion = auto.CacheWritePerMillion
            });
        }

        var tableRates = ParseModelPricingTable(markdown, warnings);
        if (tableRates.Count == 0)
        {
            warnings.Add("Model pricing table was empty or could not be parsed.");
        }

        rates.AddRange(tableRates);
        EnsureAutoFallbackRates(rates);

        if (!rates.Any(r => r.Model.Equals("Auto", StringComparison.OrdinalIgnoreCase)))
        {
            warnings.Add("Auto pricing was not found; Auto/* fallback rates were not added.");
        }

        var normalized = rates
            .Where(r => !string.IsNullOrWhiteSpace(r.Model))
            .GroupBy(r => r.Model, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .OrderBy(r => r.Model == "*" ? "\uFFFF" : r.Model == "Auto" ? "\u0000" : r.Model,
                StringComparer.OrdinalIgnoreCase)
            .ToList();

        return (normalized, warnings);
    }

    /// <summary>
    /// Newer Cursor docs list Auto as "Auto Cost" in the model table instead of a separate Auto pricing section.
    /// </summary>
    private static void EnsureAutoFallbackRates(List<CursorModelTokenRate> rates)
    {
        var auto = rates.FirstOrDefault(r =>
            r.Model.Equals("Auto", StringComparison.OrdinalIgnoreCase));
        if (auto is null)
        {
            return;
        }

        if (!rates.Any(r => r.Model == "*"))
        {
            rates.Add(new CursorModelTokenRate
            {
                Model = "*",
                InputPerMillion = auto.InputPerMillion,
                OutputPerMillion = auto.OutputPerMillion,
                CacheReadPerMillion = auto.CacheReadPerMillion,
                CacheWritePerMillion = auto.CacheWritePerMillion
            });
        }
    }

    private static string NormalizeModelName(string model)
    {
        var trimmed = model.Trim();
        if (trimmed.Equals("Auto Cost", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("AutoCost", StringComparison.OrdinalIgnoreCase))
        {
            return "Auto";
        }

        return trimmed;
    }

    private static bool TryParseAutoRates(string markdown, out CursorModelTokenRate auto)
    {
        auto = new CursorModelTokenRate { Model = "Auto" };
        var section = ExtractSection(markdown, "### Auto pricing", "### ");
        if (string.IsNullOrWhiteSpace(section))
        {
            return false;
        }

        decimal? input = null;
        decimal? output = null;
        decimal? cacheRead = null;

        foreach (var line in section.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!line.StartsWith('|') || line.Contains("---", StringComparison.Ordinal))
            {
                continue;
            }

            var cells = SplitRow(line);
            if (cells.Count < 2)
            {
                continue;
            }

            var label = cells[0];
            if (!TryParseMoney(cells[1], out var price))
            {
                continue;
            }

            if (label.Contains("Input", StringComparison.OrdinalIgnoreCase))
            {
                input = price;
            }
            else if (label.Contains("Output", StringComparison.OrdinalIgnoreCase))
            {
                output = price;
            }
            else if (label.Contains("Cache Read", StringComparison.OrdinalIgnoreCase))
            {
                cacheRead = price;
            }
        }

        if (input is null || output is null || cacheRead is null)
        {
            return false;
        }

        auto.InputPerMillion = input.Value;
        auto.OutputPerMillion = output.Value;
        auto.CacheReadPerMillion = cacheRead.Value;
        auto.CacheWritePerMillion = input.Value; // Auto prices Input + Cache Write together
        return true;
    }

    private static List<CursorModelTokenRate> ParseModelPricingTable(
        string markdown,
        List<string> warnings)
    {
        var sectionStart = markdown.IndexOf("### Model pricing", StringComparison.OrdinalIgnoreCase);
        if (sectionStart < 0)
        {
            warnings.Add("Could not find '### Model pricing' section.");
            return [];
        }

        var section = markdown[sectionStart..];
        var nextHeading = section.IndexOf("\n## ", StringComparison.Ordinal);
        if (nextHeading > 0)
        {
            section = section[..nextHeading];
        }

        var rates = new List<CursorModelTokenRate>();
        foreach (var line in section.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith('|') || trimmed.Contains("---", StringComparison.Ordinal))
            {
                continue;
            }

            var cells = SplitRow(trimmed);
            if (cells.Count < 6)
            {
                continue;
            }

            // Model | Provider | Input | Cache write | Cache read | Output | Notes
            var model = NormalizeModelName(ExtractModelName(cells[0]));
            if (string.IsNullOrWhiteSpace(model) ||
                model.Equals("Model", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!TryParseMoney(cells[2], out var input))
            {
                warnings.Add($"Skipped '{model}': could not parse input price '{cells[2]}'.");
                continue;
            }

            var cacheWrite = TryParseMoney(cells[3], out var cw) ? cw : input;
            if (!TryParseMoney(cells[4], out var cacheRead))
            {
                warnings.Add($"Skipped '{model}': could not parse cache-read price '{cells[4]}'.");
                continue;
            }

            if (!TryParseMoney(cells[5], out var output))
            {
                warnings.Add($"Skipped '{model}': could not parse output price '{cells[5]}'.");
                continue;
            }

            rates.Add(new CursorModelTokenRate
            {
                Model = model,
                InputPerMillion = input,
                CacheWritePerMillion = cacheWrite,
                CacheReadPerMillion = cacheRead,
                OutputPerMillion = output
            });
        }

        return rates;
    }

    private static string ExtractSection(string markdown, string startHeading, string nextHeadingPrefix)
    {
        var start = markdown.IndexOf(startHeading, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return string.Empty;
        }

        var from = markdown[start..];
        var afterFirstLine = from.IndexOf('\n');
        if (afterFirstLine < 0)
        {
            return from;
        }

        var body = from[(afterFirstLine + 1)..];
        var next = body.IndexOf(nextHeadingPrefix, StringComparison.Ordinal);
        return next < 0 ? body : body[..next];
    }

    private static List<string> SplitRow(string line)
    {
        var cells = line.Trim().Trim('|').Split('|');
        return cells.Select(c => c.Trim()).ToList();
    }

    private static string ExtractModelName(string cell)
    {
        var link = MarkdownLinkRegex().Match(cell);
        if (link.Success)
        {
            return link.Groups[1].Value.Trim();
        }

        return cell.Trim();
    }

    private static bool TryParseMoney(string raw, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(raw) || raw == "-" || raw == "—")
        {
            return false;
        }

        var cleaned = MoneyRegex().Replace(raw, string.Empty).Trim();
        return decimal.TryParse(
            cleaned,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out value);
    }

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]+\)", RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownLinkRegex();

    [GeneratedRegex(@"[^\d.]", RegexOptions.CultureInvariant)]
    private static partial Regex MoneyRegex();
}
