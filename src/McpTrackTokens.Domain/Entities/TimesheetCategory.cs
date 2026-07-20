using McpTrackTokens.Domain.Common;

namespace McpTrackTokens.Domain.Entities;

/// <summary>
/// A user-managed timesheet category (e.g. Work, Meetings).
/// </summary>
public sealed class TimesheetCategory : EntityBase, IAuditable
{
    /// <summary>
    /// Well-known identifier for the seeded Work category.
    /// </summary>
    public static readonly Guid WorkId = Guid.Parse("a1b2c3d4-e5f6-4789-a012-111111111101");

    /// <summary>
    /// Well-known identifier for the seeded Meetings category.
    /// </summary>
    public static readonly Guid MeetingsId = Guid.Parse("a1b2c3d4-e5f6-4789-a012-111111111102");

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the sort order for UI lists.
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// Gets or sets whether the category can be selected for new entries.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <inheritdoc />
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>
    /// Creates a new timesheet category.
    /// </summary>
    public static TimesheetCategory Create(
        string name,
        int sortOrder = 0,
        Guid? id = null,
        DateTimeOffset? createdAtUtc = null)
    {
        var now = (createdAtUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
        return new TimesheetCategory(id ?? Guid.NewGuid(), now)
        {
            Name = NormalizeName(name),
            SortOrder = sortOrder,
            IsActive = true,
            UpdatedAtUtc = now
        };
    }

    /// <summary>
    /// Renames the category.
    /// </summary>
    public void Rename(string name)
    {
        Name = NormalizeName(name);
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Updates sort order.
    /// </summary>
    public void SetSortOrder(int sortOrder)
    {
        SortOrder = sortOrder;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Activates or deactivates the category.
    /// </summary>
    public void SetActive(bool isActive)
    {
        IsActive = isActive;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TimesheetCategory"/> class.
    /// </summary>
    public TimesheetCategory()
    {
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private TimesheetCategory(Guid id, DateTimeOffset createdAtUtc)
        : base(id, createdAtUtc)
    {
        UpdatedAtUtc = createdAtUtc;
    }

    private static string NormalizeName(string name)
        => Guard.AgainstNullOrWhiteSpace(name).Trim();
}
