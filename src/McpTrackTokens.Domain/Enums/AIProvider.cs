namespace McpTrackTokens.Domain.Enums;

/// <summary>
/// AI model provider associated with usage or activity.
/// </summary>
public enum AIProvider
{
    /// <summary>Cursor-hosted models.</summary>
    Cursor = 0,

    /// <summary>OpenAI.</summary>
    OpenAI = 1,

    /// <summary>Anthropic.</summary>
    Anthropic = 2,

    /// <summary>OpenRouter.</summary>
    OpenRouter = 3,

    /// <summary>Ollama.</summary>
    Ollama = 4,

    /// <summary>LiteLLM.</summary>
    LiteLLM = 5,

    /// <summary>Any other provider.</summary>
    Other = 6
}
