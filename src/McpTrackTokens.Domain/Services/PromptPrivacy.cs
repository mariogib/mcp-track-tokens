using System.Security.Cryptography;
using System.Text;
using McpTrackTokens.Domain.Common;

namespace McpTrackTokens.Domain.Services;

/// <summary>
/// Privacy helpers for prompt content handling and non-reversible hashing.
/// </summary>
public static class PromptPrivacy
{
    /// <summary>
    /// Default: do not store prompt content.
    /// </summary>
    public const bool DefaultStorePromptContent = false;

    /// <summary>
    /// Default: do not store response/completion content.
    /// </summary>
    public const bool DefaultStoreResponseContent = false;

    /// <summary>
    /// Default: enable salted prompt hashing for duplicate detection when content is not stored.
    /// </summary>
    public const bool DefaultEnablePromptHashing = true;

    /// <summary>
    /// Computes a non-reversible SHA-256 hash of prompt content salted with the editor session id.
    /// </summary>
    /// <remarks>
    /// Hash input format: <c>{sessionId:D}:{content}</c>.
    /// The result is a lowercase hex string. Hashes must never be treated as reversible encryption.
    /// </remarks>
    /// <param name="sessionId">Editor session identifier used as salt/namespace.</param>
    /// <param name="content">Prompt content to hash.</param>
    /// <returns>Lowercase hex SHA-256 digest.</returns>
    public static string HashPrompt(Guid sessionId, string content)
    {
        Guard.AgainstEmpty(sessionId);
        Guard.AgainstNull(content);

        var payload = sessionId.ToString("D") + ":" + content;
        var bytes = Encoding.UTF8.GetBytes(payload);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Returns whether prompt content should be stored given configuration.
    /// Defaults to <see cref="DefaultStorePromptContent"/>.
    /// </summary>
    /// <param name="storePromptContent">Configured store-content flag.</param>
    public static bool ShouldStorePromptContent(bool storePromptContent = DefaultStorePromptContent)
        => storePromptContent;

    /// <summary>
    /// Returns whether a prompt hash should be computed.
    /// </summary>
    /// <param name="enablePromptHashing">Configured hashing flag.</param>
    public static bool ShouldHashPrompt(bool enablePromptHashing = DefaultEnablePromptHashing)
        => enablePromptHashing;
}
