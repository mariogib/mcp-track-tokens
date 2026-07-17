namespace McpTrackTokens.Domain.Exceptions;

/// <summary>
/// Thrown when usage attribution cannot be completed or produces an invalid result.
/// </summary>
public sealed class AttributionException : DomainException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AttributionException"/> class.
    /// </summary>
    /// <param name="message">Error message.</param>
    public AttributionException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AttributionException"/> class with an inner exception.
    /// </summary>
    /// <param name="message">Error message.</param>
    /// <param name="innerException">Inner exception.</param>
    public AttributionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
