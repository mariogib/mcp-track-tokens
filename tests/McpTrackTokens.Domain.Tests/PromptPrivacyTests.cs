using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using McpTrackTokens.Domain.Services;

namespace McpTrackTokens.Domain.Tests;

public sealed class PromptPrivacyTests
{
    [Fact]
    public void Defaults_prefer_not_storing_content_and_enable_hashing()
    {
        PromptPrivacy.DefaultStorePromptContent.Should().BeFalse();
        PromptPrivacy.DefaultStoreResponseContent.Should().BeFalse();
        PromptPrivacy.DefaultEnablePromptHashing.Should().BeTrue();

        PromptPrivacy.ShouldStorePromptContent().Should().BeFalse();
        PromptPrivacy.ShouldHashPrompt().Should().BeTrue();
    }

    [Fact]
    public void ShouldStorePromptContent_respects_explicit_flag()
    {
        PromptPrivacy.ShouldStorePromptContent(true).Should().BeTrue();
        PromptPrivacy.ShouldStorePromptContent(false).Should().BeFalse();
    }

    [Fact]
    public void HashPrompt_is_sha256_of_session_id_and_content()
    {
        var sessionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        const string content = "hello world";

        var expectedPayload = sessionId.ToString("D") + ":" + content;
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(expectedPayload)))
            .ToLowerInvariant();

        PromptPrivacy.HashPrompt(sessionId, content).Should().Be(expected);
    }

    [Fact]
    public void HashPrompt_differs_across_sessions_for_same_content()
    {
        const string content = "same prompt";
        var hashA = PromptPrivacy.HashPrompt(Guid.NewGuid(), content);
        var hashB = PromptPrivacy.HashPrompt(Guid.NewGuid(), content);

        hashA.Should().NotBe(hashB);
        hashA.Should().HaveLength(64);
    }
}
