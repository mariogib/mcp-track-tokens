namespace McpTrackTokens.Domain.Common;

/// <summary>
/// Base type for all domain entities with a unique identifier and creation timestamp.
/// </summary>
public abstract class EntityBase
{
    /// <summary>
    /// Unique identifier for the entity.
    /// </summary>
    public Guid Id { get; protected set; }

    /// <summary>
    /// UTC timestamp when the entity was created.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; protected set; }

    /// <summary>
    /// Initializes a new entity with a new identifier and current UTC timestamp.
    /// </summary>
    protected EntityBase()
    {
        Id = Guid.NewGuid();
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Initializes a new entity with an explicit identifier and creation timestamp.
    /// </summary>
    /// <param name="id">Entity identifier.</param>
    /// <param name="createdAtUtc">Creation timestamp in UTC.</param>
    protected EntityBase(Guid id, DateTimeOffset createdAtUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CreatedAtUtc = createdAtUtc == default ? DateTimeOffset.UtcNow : createdAtUtc.ToUniversalTime();
    }
}
