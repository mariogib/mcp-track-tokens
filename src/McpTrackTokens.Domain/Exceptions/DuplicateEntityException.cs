namespace McpTrackTokens.Domain.Exceptions;

/// <summary>
/// Thrown when an attempt is made to create an entity that already exists.
/// </summary>
public sealed class DuplicateEntityException : DomainException
{
    /// <summary>
    /// Gets the entity type name that is duplicated.
    /// </summary>
    public string EntityName { get; }

    /// <summary>
    /// Gets the conflicting key or natural identifier, when available.
    /// </summary>
    public object? Key { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DuplicateEntityException"/> class.
    /// </summary>
    /// <param name="entityName">Name of the duplicated entity type.</param>
    /// <param name="key">Conflicting key.</param>
    public DuplicateEntityException(string entityName, object? key = null)
        : base(BuildMessage(entityName, key))
    {
        EntityName = entityName;
        Key = key;
    }

    private static string BuildMessage(string entityName, object? key)
        => key is null
            ? $"A duplicate {entityName} already exists."
            : $"A duplicate {entityName} already exists for key '{key}'.";
}
