namespace McpTrackTokens.Domain.Exceptions;

/// <summary>
/// Base exception for all domain-layer failures.
/// </summary>
public class DomainException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DomainException"/> class.
    /// </summary>
    public DomainException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DomainException"/> class with a message.
    /// </summary>
    /// <param name="message">Error message.</param>
    public DomainException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DomainException"/> class with a message and inner exception.
    /// </summary>
    /// <param name="message">Error message.</param>
    /// <param name="innerException">Inner exception.</param>
    public DomainException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
