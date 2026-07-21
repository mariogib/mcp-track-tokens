using System.Globalization;
using System.Text;
using System.Text.Json;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Options;
using McpTrackTokens.Application.DTOs;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Application.Options;
using McpTrackTokens.Domain.Enums;
using DomainValidationException = McpTrackTokens.Domain.Exceptions.ValidationException;

namespace McpTrackTokens.Infrastructure.Export;

/// <summary>
/// Writes report payloads to disk as CSV, JSON, Markdown, or Excel-friendly CSV.
/// </summary>
public sealed class ReportExporter : IReportExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly TrackingOptions _options;

    public ReportExporter(IOptions<TrackingOptions> options)
    {
        _options = options.Value;
    }

    /// <inheritdoc />
    public byte[] Render(object report, ExportFormat format)
    {
        ArgumentNullException.ThrowIfNull(report);

        return format switch
        {
            ExportFormat.Json => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(report, report.GetType(), JsonOptions)),
            ExportFormat.Markdown => Encoding.UTF8.GetBytes(ToMarkdown(report)),
            ExportFormat.Csv => Encoding.UTF8.GetBytes(ToCsv(report, excelFriendly: false)),
            ExportFormat.ExcelCsv => Encoding.UTF8.GetBytes(ToCsv(report, excelFriendly: true)),
            _ => throw new DomainValidationException(nameof(format), $"Unsupported export format '{format}'.")
        };
    }

    /// <inheritdoc />
    public async Task<ExportResultDto> ExportAsync(
        object report,
        ExportFormat format,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var fullPath = Path.GetFullPath(TrackingOptions.ExpandPath(filePath));
        EnsureApprovedPath(fullPath);

        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var bytes = Render(report, format);
        await File.WriteAllBytesAsync(fullPath, bytes, cancellationToken).ConfigureAwait(false);

        return new ExportResultDto
        {
            FilePath = fullPath,
            Format = format,
            ByteCount = bytes.LongLength,
            ExportedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private void EnsureApprovedPath(string fullPath)
    {
        if (fullPath.Contains("..", StringComparison.Ordinal))
        {
            throw new DomainValidationException(nameof(fullPath), "Path traversal is not allowed.");
        }

        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new DomainValidationException(nameof(fullPath), "Invalid export file path.");

        var normalizedDirectory = Path.GetFullPath(directory);
        if (!normalizedDirectory.EndsWith(Path.DirectorySeparatorChar) &&
            !normalizedDirectory.EndsWith(Path.AltDirectorySeparatorChar))
        {
            normalizedDirectory += Path.DirectorySeparatorChar;
        }

        var approved = _options.GetApprovedExportRoots();
        if (!approved.Any(root => normalizedDirectory.StartsWith(root, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainValidationException(
                nameof(fullPath),
                "Export path must be under an approved export directory.");
        }
    }

    private static string ToMarkdown(object report)
    {
        var json = JsonSerializer.Serialize(report, report.GetType(), JsonOptions);
        using var document = JsonDocument.Parse(json);
        var sb = new StringBuilder();
        sb.AppendLine($"# {report.GetType().Name}");
        sb.AppendLine();
        WriteMarkdownElement(sb, document.RootElement, 2);
        return sb.ToString();
    }

    private static void WriteMarkdownElement(StringBuilder sb, JsonElement element, int headingLevel)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    sb.AppendLine($"{new string('#', Math.Min(headingLevel, 6))} {property.Name}");
                    sb.AppendLine();
                    WriteMarkdownElement(sb, property.Value, headingLevel + 1);
                }

                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    index++;
                    sb.AppendLine($"- Item {index}");
                    if (item.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    {
                        WriteMarkdownElement(sb, item, headingLevel + 1);
                    }
                    else
                    {
                        sb.AppendLine($"  {JsonValueToString(item)}");
                    }
                }

                sb.AppendLine();
                break;
            default:
                sb.AppendLine(JsonValueToString(element));
                sb.AppendLine();
                break;
        }
    }

    private static string ToCsv(object report, bool excelFriendly)
    {
        var json = JsonSerializer.Serialize(report, report.GetType(), JsonOptions);
        using var document = JsonDocument.Parse(json);

        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        if (excelFriendly)
        {
            writer.Write('\uFEFF');
        }

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true
        };

        using var csv = new CsvWriter(writer, config);

        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            WriteArrayAsCsv(csv, document.RootElement);
        }
        else if (document.RootElement.ValueKind == JsonValueKind.Object)
        {
            // Prefer the first array-valued property as a tabular section.
            JsonElement? table = null;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    table = property.Value;
                    break;
                }
            }

            if (table is not null)
            {
                WriteArrayAsCsv(csv, table.Value);
            }
            else
            {
                csv.WriteField("Property");
                csv.WriteField("Value");
                csv.NextRecord();
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    csv.WriteField(property.Name);
                    csv.WriteField(JsonValueToString(property.Value));
                    csv.NextRecord();
                }
            }
        }
        else
        {
            csv.WriteField("Value");
            csv.NextRecord();
            csv.WriteField(JsonValueToString(document.RootElement));
            csv.NextRecord();
        }

        csv.Flush();
        return writer.ToString();
    }

    private static void WriteArrayAsCsv(CsvWriter csv, JsonElement array)
    {
        var rows = array.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object).ToList();
        if (rows.Count == 0)
        {
            csv.WriteField("Value");
            csv.NextRecord();
            foreach (var item in array.EnumerateArray())
            {
                csv.WriteField(JsonValueToString(item));
                csv.NextRecord();
            }

            return;
        }

        var headers = new List<string>();
        foreach (var row in rows)
        {
            foreach (var property in row.EnumerateObject())
            {
                if (!headers.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                {
                    headers.Add(property.Name);
                }
            }
        }

        foreach (var header in headers)
        {
            csv.WriteField(header);
        }

        csv.NextRecord();

        foreach (var row in rows)
        {
            foreach (var header in headers)
            {
                if (row.TryGetProperty(header, out var value))
                {
                    csv.WriteField(JsonValueToString(value));
                }
                else
                {
                    csv.WriteField(string.Empty);
                }
            }

            csv.NextRecord();
        }
    }

    private static string JsonValueToString(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number => element.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => string.Empty,
            _ => element.GetRawText()
        };
}
