namespace McpTrackTokens.Domain.Enums;

/// <summary>
/// Supported report export formats.
/// </summary>
public enum ExportFormat
{
    /// <summary>Comma-separated values.</summary>
    Csv = 0,

    /// <summary>JSON document.</summary>
    Json = 1,

    /// <summary>Markdown document.</summary>
    Markdown = 2,

    /// <summary>Excel-compatible CSV.</summary>
    ExcelCsv = 3
}
