using FluentAssertions;
using McpTrackTokens.Domain.Exceptions;
using McpTrackTokens.Domain.Validation;

namespace McpTrackTokens.Domain.Tests;

public sealed class ProjectValidatorTests
{
    [Theory]
    [InlineData("My Project")]
    [InlineData("a")]
    [InlineData("Project with spaces and numbers 123")]
    public void ValidateName_accepts_valid_names(string name)
    {
        var act = () => ProjectValidator.ValidateName(name);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateName_rejects_missing_names(string? name)
    {
        var act = () => ProjectValidator.ValidateName(name);
        act.Should().Throw<ValidationException>().Which.PropertyName.Should().Be("name");
    }

    [Fact]
    public void ValidateName_rejects_names_exceeding_max_length()
    {
        var name = new string('x', ProjectValidator.MaxNameLength + 1);
        var act = () => ProjectValidator.ValidateName(name);
        act.Should().Throw<ValidationException>().Which.PropertyName.Should().Be("name");
    }

    [Theory]
    [InlineData("my-project")]
    [InlineData("project")]
    [InlineData("a1-b2")]
    [InlineData("abc123")]
    public void ValidateSlug_accepts_valid_slugs(string slug)
    {
        var act = () => ProjectValidator.ValidateSlug(slug);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("My-Project")]
    [InlineData("-leading")]
    [InlineData("trailing-")]
    [InlineData("has_underscore")]
    [InlineData("has space")]
    public void ValidateSlug_rejects_invalid_slugs(string? slug)
    {
        var act = () => ProjectValidator.ValidateSlug(slug);
        act.Should().Throw<ValidationException>().Which.PropertyName.Should().Be("slug");
    }

    [Fact]
    public void ValidateSlug_rejects_slugs_exceeding_max_length()
    {
        var slug = new string('a', ProjectValidator.MaxSlugLength + 1);
        var act = () => ProjectValidator.ValidateSlug(slug);
        act.Should().Throw<ValidationException>().Which.PropertyName.Should().Be("slug");
    }

    [Theory]
    [InlineData("USD")]
    [InlineData("eur")]
    [InlineData("Gbp")]
    public void ValidateCurrency_accepts_three_letter_codes(string currency)
    {
        var act = () => ProjectValidator.ValidateCurrency(currency);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("US")]
    [InlineData("USDD")]
    [InlineData("12A")]
    [InlineData("U$D")]
    public void ValidateCurrency_rejects_invalid_codes(string? currency)
    {
        var act = () => ProjectValidator.ValidateCurrency(currency);
        act.Should().Throw<ValidationException>().Which.PropertyName.Should().Be("currency");
    }

    [Fact]
    public void Slugify_derives_slug_from_display_name()
    {
        ProjectValidator.Slugify("My Cool Project!").Should().Be("my-cool-project");
    }
}
