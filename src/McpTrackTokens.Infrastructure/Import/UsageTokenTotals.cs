namespace McpTrackTokens.Infrastructure.Import;

/// <summary>
/// Provides consistent token-total derivation for imported usage records.
/// </summary>
internal static class UsageTokenTotals
{
    /// <summary>
    /// Keeps an explicit total, or derives one when at least one token component is supplied.
    /// </summary>
    public static long? DeriveTotalIfMissing(
        long? totalTokens,
        long? inputTokens,
        long? outputTokens,
        long? cachedInputTokens,
        long? cacheWriteTokens,
        long? reasoningTokens)
    {
        if (totalTokens is not null)
        {
            return totalTokens;
        }

        if (inputTokens is null &&
            outputTokens is null &&
            cachedInputTokens is null &&
            cacheWriteTokens is null &&
            reasoningTokens is null)
        {
            return null;
        }

        return (inputTokens ?? 0) +
               (outputTokens ?? 0) +
               (cachedInputTokens ?? 0) +
               (cacheWriteTokens ?? 0) +
               (reasoningTokens ?? 0);
    }
}
