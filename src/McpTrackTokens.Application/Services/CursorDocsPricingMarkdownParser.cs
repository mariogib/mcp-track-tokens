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
        EnsureAutoFallbackRates(rates, warnings);

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
        EnsureAutoFallbackRates(rates, warnings);

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
    /// True when the rate card includes first-party Cursor pool models (Grok or Composer).
    /// </summary>
    public static bool HasCursorPoolRates(IReadOnlyList<CursorModelTokenRate> rates)
        => rates.Any(r => IsCursorPoolModel(r.Model));

    public static bool IsCursorPoolModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return false;
        }

        var name = model.Trim();
        return name.StartsWith("Grok", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("Composer", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Newer Cursor docs list Auto as "Auto Cost" in the model table instead of a separate Auto pricing section.
    /// When neither is present, keep the last published Auto Cost rates so Get Rates still seeds Auto/*.
    /// </summary>
    private static void EnsureAutoFallbackRates(List<CursorModelTokenRate> rates, List<string>? warnings = null)
    {
        var auto = rates.FirstOrDefault(r =>
            r.Model.Equals("Auto", StringComparison.OrdinalIgnoreCase));
        if (auto is null)
        {
            var defaults = CursorTokenCostCalculator.CreateDefaultRates();
            rates.AddRange(defaults);
            warnings?.Add("Auto pricing was not listed in Cursor docs; used built-in Auto Cost fallback rates.");
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
        var rates = new List<CursorModelTokenRate>();
        var cursorSection = ExtractSection(markdown, "## Cursor Models", "\n## ");
        if (!string.IsNullOrWhiteSpace(cursorSection))
        {
            var cursorRates = ParseAllModelTables(cursorSection, warnings);
            if (cursorRates.Count == 0)
            {
                warnings.Add("Found a Cursor Models section but could not parse its pricing table.");
            }

            rates.AddRange(cursorRates);
        }

        var otherStart = markdown.IndexOf("## Other Models", StringComparison.OrdinalIgnoreCase);
        if (otherStart < 0)
        {
            otherStart = markdown.IndexOf("### Model pricing", StringComparison.OrdinalIgnoreCase);
        }

        if (otherStart >= 0)
        {
            rates.AddRange(ParseAllModelTables(markdown[otherStart..], warnings));
        }
        else if (rates.Count == 0)
        {
            rates.AddRange(ParseAllModelTables(markdown, warnings));
        }

        if (!rates.Any(r => IsCursorPoolModel(r.Model)))
        {
            var fallback = ParseAllModelTables(markdown, warnings)
                .Where(r => IsCursorPoolModel(r.Model))
                .ToList();
            rates.InsertRange(0, fallback);
        }

        if (rates.Count == 0)
        {
            warnings.Add("Could not find a Model pricing table.");
        }

        return rates;
    }

    private static List<CursorModelTokenRate> ParseAllModelTables(
        string markdown,
        List<string> warnings)
    {
        var rates = new List<CursorModelTokenRate>();
        var lines = markdown.Replace("\r\n", "\n").Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (!trimmed.StartsWith('|'))
            {
                continue;
            }

            var headerCells = SplitRow(trimmed);
            if (!TryMapPricingColumns(headerCells, out var columns))
            {
                continue;
            }

            i++;
            for (; i < lines.Length; i++)
            {
                var row = lines[i].Trim();
                if (!row.StartsWith('|'))
                {
                    break;
                }

                if (IsMarkdownSeparatorRow(row))
                {
                    continue;
                }

                var cells = SplitRow(row);
                if (cells.Count <= columns.Model)
                {
                    continue;
                }

                var model = NormalizeModelName(ExtractModelName(cells[columns.Model]));
                if (string.IsNullOrWhiteSpace(model) ||
                    model.Equals("Model", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!TryGetMoney(cells, columns.Input, out var input))
                {
                    warnings.Add($"Skipped '{model}': could not parse input price.");
                    continue;
                }

                var cacheWrite = TryGetMoney(cells, columns.CacheWrite, out var cw) ? cw : input;
                if (!TryGetMoney(cells, columns.CacheRead, out var cacheRead))
                {
                    warnings.Add($"Skipped '{model}': could not parse cache-read price.");
                    continue;
                }

                if (!TryGetMoney(cells, columns.Output, out var output))
                {
                    warnings.Add($"Skipped '{model}': could not parse output price.");
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
        }

        return rates;
    }

    private static bool TryMapPricingColumns(IReadOnlyList<string> cells, out PricingColumns columns)
    {
        columns = default;
        if (cells.Count < 4)
        {
            return false;
        }

        var model = IndexOfColumn(cells, "Model");
        var input = IndexOfColumn(cells, "Input");
        var output = IndexOfColumn(cells, "Output");
        var cacheRead = IndexOfColumn(cells, "Cache read");
        var cacheWrite = IndexOfColumn(cells, "Cache write");
        if (model < 0 || input < 0 || output < 0 || cacheRead < 0)
        {
            return false;
        }

        columns = new PricingColumns(model, input, output, cacheRead, cacheWrite);
        return true;
    }

    private static int IndexOfColumn(IReadOnlyList<string> cells, string name)
    {
        for (var i = 0; i < cells.Count; i++)
        {
            if (cells[i].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool TryGetMoney(IReadOnlyList<string> cells, int index, out decimal value)
    {
        value = 0m;
        if (index < 0 || index >= cells.Count)
        {
            return false;
        }

        return TryParseMoney(cells[index], out value);
    }

    private static bool IsMarkdownSeparatorRow(string row)
    {
        var cells = SplitRow(row);
        return cells.Count > 0 &&
               cells.All(cell => cell.Length == 0 || cell.All(ch => ch is '-' or ':' or ' '));
    }

    private readonly record struct PricingColumns(
        int Model,
        int Input,
        int Output,
        int CacheRead,
        int CacheWrite);

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
