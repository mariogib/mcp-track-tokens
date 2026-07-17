namespace McpTrackTokens.Domain.Enums;

/// <summary>
/// Reporting period granularity.
/// </summary>
public enum ReportingPeriod
{
    /// <summary>Daily period.</summary>
    Day = 0,

    /// <summary>Weekly period.</summary>
    Week = 1,

    /// <summary>Monthly period.</summary>
    Month = 2,

    /// <summary>Quarterly period.</summary>
    Quarter = 3,

    /// <summary>Yearly period.</summary>
    Year = 4,

    /// <summary>Custom date range.</summary>
    Custom = 5
}
