namespace McpTrackTokens.Domain.Exceptions;

/// <summary>
/// Thrown when a requested domain entity cannot be found.
/// </summary>
public sealed class EntityNotFoundException : DomainException
{
    /// <summary>
    /// Gets the entity type name that was not found.
    /// </summary>
    public string EntityName { get; }

    /// <summary>
    /// Gets the identifier that was looked up, when available.
    /// </summary>
    public object? Key { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="EntityNotFoundException"/> class.
    /// </summary>
    /// <param name="entityName">Name of the missing entity type.</param>
    /// <param name="key">Lookup key.</param>
    public EntityNotFoundException(string entityName, object? key = null)
        : base(BuildMessage(entityName, key))
    {
        EntityName = entityName;
        Key = key;
    }

    private static string BuildMessage(string entityName, object? key)
        => key is null
            ? $"{entityName} was not found."
            : $"{entityName} with key '{key}' was not found.";
}
