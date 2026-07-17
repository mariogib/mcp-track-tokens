namespace McpTrackTokens.Domain.Enums;

/// <summary>
/// Origin of an external usage or cost record.
/// </summary>
public enum UsageSource
{
    /// <summary>Cursor CSV export.</summary>
    CursorCsv = 0,

    /// <summary>Cursor JSON export.</summary>
    CursorJson = 1,

    /// <summary>Cursor API.</summary>
    CursorApi = 2,

    /// <summary>Manual entry.</summary>
    Manual = 3,

    /// <summary>LiteLLM proxy.</summary>
    LiteLLM = 4,

    /// <summary>OpenAI usage.</summary>
    OpenAI = 5,

    /// <summary>Anthropic usage.</summary>
    Anthropic = 6,

    /// <summary>OpenRouter usage.</summary>
    OpenRouter = 7,

    /// <summary>Ollama local usage.</summary>
    Ollama = 8,

    /// <summary>Any other source.</summary>
    Other = 9
}
