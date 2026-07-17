using FluentAssertions;
using McpTrackTokens.Domain.ValueObjects;

namespace McpTrackTokens.Domain.Tests;

public sealed class NormalizedRemoteUrlTests
{
    [Theory]
    [InlineData("https://github.com/owner/repo.git", "https://github.com/owner/repo")]
    [InlineData("https://GitHub.com/Owner/Repo", "https://github.com/Owner/Repo")]
    [InlineData("http://github.com/owner/repo.git", "https://github.com/owner/repo")]
    public void Normalize_https_urls(string input, string expected)
    {
        NormalizedRemoteUrl.Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("git@github.com:owner/repo.git", "ssh://github.com/owner/repo")]
    [InlineData("git@github.com:owner/repo", "ssh://github.com/owner/repo")]
    [InlineData("git@GitHub.com:Org/Project.git", "ssh://github.com/Org/Project")]
    public void Normalize_scp_like_ssh_urls(string input, string expected)
    {
        NormalizedRemoteUrl.Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("ssh://git@github.com/owner/repo.git", "ssh://github.com/owner/repo")]
    [InlineData("ssh://github.com/owner/repo", "ssh://github.com/owner/repo")]
    public void Normalize_ssh_scheme_urls(string input, string expected)
    {
        NormalizedRemoteUrl.Normalize(input).Should().Be(expected);
    }

    [Fact]
    public void Normalize_strips_trailing_git_suffix()
    {
        NormalizedRemoteUrl.Normalize("https://github.com/a/b.GIT")
            .Should().Be("https://github.com/a/b");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Normalize_returns_empty_for_null_or_whitespace(string? url)
    {
        NormalizedRemoteUrl.Normalize(url).Should().BeEmpty();
    }

    [Fact]
    public void Equals_is_case_insensitive()
    {
        var left = NormalizedRemoteUrl.Create("https://github.com/Owner/Repo");
        var right = NormalizedRemoteUrl.Create("HTTPS://GITHUB.COM/owner/repo");
        left.Should().Be(right);
    }
}
