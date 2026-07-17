namespace McpTrackTokens.Domain.Exceptions;

/// <summary>
/// Thrown when domain validation fails for one or more properties.
/// </summary>
public sealed class ValidationException : DomainException
{
    /// <summary>
    /// Gets the property or parameter that failed validation, when known.
    /// </summary>
    public string? PropertyName { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationException"/> class.
    /// </summary>
    /// <param name="message">Validation error message.</param>
    public ValidationException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationException"/> class for a specific property.
    /// </summary>
    /// <param name="propertyName">Property or parameter name.</param>
    /// <param name="message">Validation error message.</param>
    public ValidationException(string propertyName, string message)
        : base(string.IsNullOrWhiteSpace(propertyName) ? message : $"{propertyName}: {message}")
    {
        PropertyName = propertyName;
    }
}
