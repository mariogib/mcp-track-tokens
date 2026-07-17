using FluentAssertions;
using McpTrackTokens.Domain.ValueObjects;

namespace McpTrackTokens.Domain.Tests;

public sealed class NormalizedPathTests
{
    [Theory]
    [InlineData(@"C:\Users\dev\repo", "C:/Users/dev/repo")]
    [InlineData(@"c:\Users\dev\repo\", "C:/Users/dev/repo")]
    [InlineData(@"D:\src\\nested", "D:/src/nested")]
    public void Normalize_converts_windows_paths_and_uppercases_drive(string input, string expected)
    {
        NormalizedPath.Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("/home/dev/repo", "/home/dev/repo")]
    [InlineData("/home/dev/repo/", "/home/dev/repo")]
    [InlineData("/var//log/app", "/var/log/app")]
    public void Normalize_handles_unix_paths(string input, string expected)
    {
        NormalizedPath.Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("c:/projects/app", "C:/projects/app")]
    [InlineData("e:\\work\\code", "E:/work/code")]
    public void Normalize_uppercases_drive_letter(string input, string expected)
    {
        NormalizedPath.Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(@"\\server\share\folder", "//server/share/folder")]
    [InlineData("//server/share/folder/", "//server/share/folder")]
    [InlineData(@"\\server\share", "//server/share")]
    public void Normalize_preserves_unc_prefix(string input, string expected)
    {
        NormalizedPath.Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(@"C:\repo\", "C:/repo")]
    [InlineData("/tmp/data/", "/tmp/data")]
    [InlineData("//server/share/path/", "//server/share/path")]
    public void Normalize_trims_trailing_separators(string input, string expected)
    {
        NormalizedPath.Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_returns_empty_for_null_or_whitespace(string? path)
    {
        NormalizedPath.Normalize(path).Should().BeEmpty();
    }

    [Fact]
    public void Equals_is_case_insensitive()
    {
        var left = NormalizedPath.Create(@"C:\Users\Dev\Repo");
        var right = NormalizedPath.Create(@"c:\users\dev\repo");
        left.Should().Be(right);
        (left == right).Should().BeTrue();
    }
}
