using McpTrackTokens.Application.Interfaces;

namespace McpTrackTokens.Infrastructure.Import;

/// <summary>
/// Maps raw Cursor usage column headers to canonical field names.
/// </summary>
public sealed class CursorUsageColumnMapper : ICursorUsageColumnMapper
{
    private static readonly Dictionary<string, string[]> CanonicalAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TimestampUtc"] =
        [
            "timestamputc", "timestamp", "date", "datetime", "day", "time", "createdat", "usagedate"
        ],
        ["Model"] = ["model", "modelname"],
        ["InputTokens"] =
        [
            "inputtokens", "prompttokens",
            // Cursor dashboard export (input without cache write)
            "inputwocachewrite", "inputwithoutcachewrite"
        ],
        ["OutputTokens"] = ["outputtokens", "completiontokens"],
        ["TotalTokens"] = ["totaltokens", "tokens", "tokencount"],
        ["CachedInputTokens"] =
        [
            "cachedinputtokens", "cachetokens", "cacheread"
        ],
        ["CacheWriteTokens"] =
        [
            "cachewritetokens", "cachewrite",
            // Cursor dashboard export
            "inputwcachewrite", "inputwithcachewrite"
        ],
        ["ReasoningTokens"] = ["reasoningtokens"],
        ["ReportedCost"] =
        [
            "reportedcost", "cost", "amount", "usagecost", "price", "totalcost", "apicost", "costtoyou"
        ],
        ["Currency"] = ["currency", "curr", "ccy"],
        ["RequestCount"] = ["requestcount", "requests", "qty", "quantity"],
        ["ExternalRecordId"] =
        [
            "externalrecordid", "recordid", "usageid"
        ],
        ["UserIdentifier"] = ["useridentifier", "user", "user_id", "userid", "email", "account"],
        ["Provider"] = ["provider", "ai_provider", "aiprovider"],
        ["ExternalSessionId"] = ["externalsessionid", "sessionid", "session_id", "session"],
        ["ExternalRequestId"] = ["externalrequestid", "requestid", "request_id"],
        ["ExternalConversationId"] =
        [
            "externalconversationid", "conversationid", "conversation_id", "chatid", "chat_id"
        ],
        ["PeriodStartUtc"] = ["periodstartutc", "period_start", "periodstart", "start", "from"],
        ["PeriodEndUtc"] = ["periodendutc", "period_end", "periodend", "end", "to"],
        ["RepositoryPath"] = ["repositorypath", "repo", "repository", "repo_path", "path"],
        ["RemoteUrl"] = ["remoteurl", "remote", "git_remote", "remote_url"]
    };

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> MapColumns(
        IReadOnlyList<string> columns,
        IReadOnlyDictionary<string, string>? overrides = null)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (overrides is not null)
        {
            foreach (var pair in overrides)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) ||
                    string.IsNullOrWhiteSpace(pair.Value) ||
                    string.Equals(pair.Value.Trim(), "ignore", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Dashboard sends source column → canonical field.
                result[pair.Value.Trim()] = pair.Key.Trim();
            }
        }

        foreach (var column in columns)
        {
            if (string.IsNullOrWhiteSpace(column))
            {
                continue;
            }

            if (result.Values.Any(v => string.Equals(v, column, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var normalized = NormalizeHeader(column);
            foreach (var (canonical, aliases) in CanonicalAliases)
            {
                if (result.ContainsKey(canonical))
                {
                    continue;
                }

                if (aliases.Any(a => string.Equals(NormalizeHeader(a), normalized, StringComparison.OrdinalIgnoreCase)))
                {
                    result[canonical] = column;
                    break;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Determines whether a source header is an alias for a canonical import field.
    /// </summary>
    internal static bool HeaderMatchesCanonical(string header, string canonical)
    {
        if (!CanonicalAliases.TryGetValue(canonical, out var aliases))
        {
            return false;
        }

        var normalizedHeader = NormalizeHeader(header);
        return aliases.Any(alias =>
            string.Equals(NormalizeHeader(alias), normalizedHeader, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Determines whether the supplied columns contain a canonical field or one of its aliases.
    /// </summary>
    internal static bool AnyColumnMatchesCanonical(
        IEnumerable<string> columns,
        string canonical)
        => columns.Any(column => HeaderMatchesCanonical(column, canonical));

    /// <summary>
    /// Lists source columns that have been mapped or recognized as standard import fields.
    /// </summary>
    internal static HashSet<string> GetKnownColumns(
        IReadOnlyDictionary<string, string> mappings,
        IEnumerable<string> columns)
    {
        var known = new HashSet<string>(mappings.Values, StringComparer.OrdinalIgnoreCase);
        foreach (var column in columns)
        {
            if (CanonicalAliases.Keys.Any(canonical => HeaderMatchesCanonical(column, canonical)))
            {
                known.Add(column);
            }
        }

        return known;
    }

    /// <summary>
    /// Normalizes a header for alias matching by keeping letters and digits only.
    /// </summary>
    internal static string NormalizeHeader(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        Span<char> buffer = stackalloc char[value.Length];
        var length = 0;
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                buffer[length++] = ch;
            }
        }

        return new string(buffer[..length]);
    }
}
