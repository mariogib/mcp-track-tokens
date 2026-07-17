using McpTrackTokens.Domain.Common;
using McpTrackTokens.Domain.Enums;

namespace McpTrackTokens.Domain.Entities;

/// <summary>
/// A configurable rule for allocating subscription or usage costs across projects.
/// </summary>
public sealed class CostAllocationRule : EntityBase, IAuditable
{
    /// <summary>
    /// Gets or sets the rule name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the evaluation priority (lower runs first).
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    /// Gets or sets the allocation rule type.
    /// </summary>
    public AllocationRuleType RuleType { get; set; }

    /// <summary>
    /// Gets or sets rule-specific configuration as JSON.
    /// </summary>
    public string? ConfigurationJson { get; set; }

    /// <summary>
    /// Gets or sets whether the rule is enabled.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <inheritdoc />
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>
    /// Creates a cost allocation rule.
    /// </summary>
    public static CostAllocationRule Create(
        string name,
        AllocationRuleType ruleType,
        int priority = 0,
        string? configurationJson = null,
        bool isEnabled = true,
        Guid? id = null,
        DateTimeOffset? createdAtUtc = null)
    {
        var now = createdAtUtc?.ToUniversalTime() ?? DateTimeOffset.UtcNow;
        return new CostAllocationRule(id ?? Guid.NewGuid(), now)
        {
            Name = Guard.AgainstNullOrWhiteSpace(name).Trim(),
            RuleType = ruleType,
            Priority = priority,
            ConfigurationJson = configurationJson,
            IsEnabled = isEnabled,
            UpdatedAtUtc = now
        };
    }

    /// <summary>
    /// Marks the rule as updated.
    /// </summary>
    public void Touch(DateTimeOffset? updatedAtUtc = null)
    {
        UpdatedAtUtc = updatedAtUtc?.ToUniversalTime() ?? DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CostAllocationRule"/> class.
    /// </summary>
    public CostAllocationRule()
    {
        UpdatedAtUtc = CreatedAtUtc;
    }

    private CostAllocationRule(Guid id, DateTimeOffset createdAtUtc)
        : base(id, createdAtUtc)
    {
        UpdatedAtUtc = createdAtUtc;
    }
}
