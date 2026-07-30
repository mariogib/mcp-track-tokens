using McpTrackTokens.Domain.Common;
using McpTrackTokens.Domain.Validation;

namespace McpTrackTokens.Domain.Entities;

/// <summary>
/// A billable or tracked project with a single repository, plus sessions and usage.
/// </summary>
public sealed class Project : EntityBase, IAuditable
{
    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the URL-safe slug.
    /// </summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional client name.
    /// </summary>
    public string? ClientName { get; set; }

    /// <summary>
    /// Gets or sets the optional billing code.
    /// </summary>
    public string? BillingCode { get; set; }

    /// <summary>
    /// Gets or sets the ISO currency code used for cost reporting.
    /// </summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Gets or sets the primary local repository path.
    /// </summary>
    public string? PrimaryRepositoryPath { get; set; }

    /// <summary>
    /// Gets or sets the primary remote Git URL.
    /// </summary>
    public string? PrimaryRemoteUrl { get; set; }

    /// <inheritdoc />
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets whether the project is active.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Creates a new project with validated core fields.
    /// </summary>
    public static Project Create(
        string name,
        string slug,
        string currency = "USD",
        string? clientName = null,
        string? billingCode = null,
        string? primaryRepositoryPath = null,
        string? primaryRemoteUrl = null,
        Guid? id = null,
        DateTimeOffset? createdAtUtc = null)
    {
        ProjectValidator.ValidateName(name);
        ProjectValidator.ValidateSlug(slug);
        ProjectValidator.ValidateCurrency(currency);

        var now = createdAtUtc?.ToUniversalTime() ?? DateTimeOffset.UtcNow;
        return new Project(id ?? Guid.NewGuid(), now)
        {
            Name = name.Trim(),
            Slug = slug.Trim().ToLowerInvariant(),
            Currency = currency.Trim().ToUpperInvariant(),
            ClientName = string.IsNullOrWhiteSpace(clientName) ? null : clientName.Trim(),
            BillingCode = string.IsNullOrWhiteSpace(billingCode) ? null : billingCode.Trim(),
            PrimaryRepositoryPath = primaryRepositoryPath,
            PrimaryRemoteUrl = primaryRemoteUrl,
            UpdatedAtUtc = now,
            IsActive = true
        };
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Project"/> class.
    /// </summary>
    public Project()
    {
        UpdatedAtUtc = CreatedAtUtc;
    }

    private Project(Guid id, DateTimeOffset createdAtUtc)
        : base(id, createdAtUtc)
    {
        UpdatedAtUtc = createdAtUtc;
    }

    /// <summary>
    /// Marks the project as updated at the current UTC time.
    /// </summary>
    public void Touch(DateTimeOffset? updatedAtUtc = null)
    {
        UpdatedAtUtc = updatedAtUtc?.ToUniversalTime() ?? DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Updates editable project fields.
    /// </summary>
    public void UpdateDetails(
        string name,
        string slug,
        string currency,
        string? clientName,
        string? billingCode,
        string? primaryRepositoryPath,
        string? primaryRemoteUrl,
        bool isActive,
        DateTimeOffset? updatedAtUtc = null)
    {
        ProjectValidator.ValidateName(name);
        ProjectValidator.ValidateSlug(slug);
        ProjectValidator.ValidateCurrency(currency);

        Name = name.Trim();
        Slug = slug.Trim().ToLowerInvariant();
        Currency = currency.Trim().ToUpperInvariant();
        ClientName = string.IsNullOrWhiteSpace(clientName) ? null : clientName.Trim();
        BillingCode = string.IsNullOrWhiteSpace(billingCode) ? null : billingCode.Trim();
        PrimaryRepositoryPath = string.IsNullOrWhiteSpace(primaryRepositoryPath)
            ? null
            : primaryRepositoryPath.Trim();
        PrimaryRemoteUrl = string.IsNullOrWhiteSpace(primaryRemoteUrl) ? null : primaryRemoteUrl.Trim();
        IsActive = isActive;
        Touch(updatedAtUtc);
    }

    /// <summary>
    /// Soft-deletes the project by marking it inactive.
    /// </summary>
    public void Deactivate(DateTimeOffset? updatedAtUtc = null)
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        Touch(updatedAtUtc);
    }
}
