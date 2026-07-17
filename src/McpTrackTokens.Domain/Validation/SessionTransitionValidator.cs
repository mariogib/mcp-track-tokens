using McpTrackTokens.Domain.Enums;
using McpTrackTokens.Domain.Exceptions;

namespace McpTrackTokens.Domain.Validation;

/// <summary>
/// Validates allowed <see cref="SessionStatus"/> transitions for editor sessions.
/// </summary>
public static class SessionTransitionValidator
{
    /// <summary>
    /// Returns whether a transition from <paramref name="from"/> to <paramref name="to"/> is allowed.
    /// </summary>
    public static bool CanTransition(SessionStatus from, SessionStatus to)
    {
        if (from == to)
        {
            return true;
        }

        return from switch
        {
            SessionStatus.Active => to is SessionStatus.Paused or SessionStatus.Ended or SessionStatus.Abandoned,
            SessionStatus.Paused => to is SessionStatus.Active or SessionStatus.Ended or SessionStatus.Abandoned,
            SessionStatus.Ended => false,
            SessionStatus.Abandoned => false,
            _ => false
        };
    }

    /// <summary>
    /// Throws <see cref="ValidationException"/> when the transition is not allowed.
    /// </summary>
    public static void EnsureCanTransition(SessionStatus from, SessionStatus to)
    {
        if (!CanTransition(from, to))
        {
            throw new ValidationException(
                nameof(SessionStatus),
                $"Cannot transition session from {from} to {to}.");
        }
    }

    /// <summary>
    /// Returns the set of statuses reachable from <paramref name="from"/> (excluding itself).
    /// </summary>
    public static IReadOnlyCollection<SessionStatus> GetAllowedTargets(SessionStatus from)
        => from switch
        {
            SessionStatus.Active => new[] { SessionStatus.Paused, SessionStatus.Ended, SessionStatus.Abandoned },
            SessionStatus.Paused => new[] { SessionStatus.Active, SessionStatus.Ended, SessionStatus.Abandoned },
            _ => Array.Empty<SessionStatus>()
        };
}
