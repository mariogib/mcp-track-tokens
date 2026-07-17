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
            "timestamputc", "timestamp", "date", "datetime", "day", "time", "createdat", "created_at", "usage date"
        ],
        ["Model"] = ["model", "modelname", "model_name", "model name"],
        ["InputTokens"] =
        [
            "inputtokens", "input_tokens", "input tokens", "prompttokens", "prompt_tokens", "prompt tokens"
        ],
        ["OutputTokens"] =
        [
            "outputtokens", "output_tokens", "output tokens", "completiontokens", "completion_tokens",
            "completion tokens"
        ],
        ["TotalTokens"] = ["totaltokens", "total_tokens", "total tokens", "tokens", "token count", "tokencount"],
        ["CachedInputTokens"] =
        [
            "cachedinputtokens", "cached_input_tokens", "cached input tokens", "cache tokens", "cachetokens"
        ],
        ["ReasoningTokens"] = ["reasoningtokens", "reasoning_tokens", "reasoning tokens"],
        ["ReportedCost"] =
        [
            "reportedcost", "cost", "amount", "usage cost", "usagecost", "price", "total cost", "totalcost"
        ],
        ["Currency"] = ["currency", "curr", "ccy"],
        ["RequestCount"] = ["requestcount", "requests", "request count", "request_count", "qty", "quantity"],
        ["ExternalRecordId"] =
        [
            "externalrecordid", "external_record_id", "id", "recordid", "record_id", "usageid", "usage_id"
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
                if (!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                {
                    result[pair.Value.Trim()] = pair.Key.Trim();
                }
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

    private static string NormalizeHeader(string value)
        => value.Trim().Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
}
