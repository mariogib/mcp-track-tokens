using FluentAssertions;
using McpTrackTokens.Application.Services;

namespace McpTrackTokens.Application.Tests;

public sealed class CursorDocsPricingMarkdownParserTests
{
    private const string SampleMarkdown = """
        # Models & Pricing

        ### Auto pricing

        | Token type          | Price per 1M tokens |
        | :------------------ | :------------------ |
        | Input + Cache Write | $1.25               |
        | Output              | $6.00               |
        | Cache Read          | $0.25               |

        ### Composer pricing

        Composer 2.5 is Cursor's own model.

        ### Model pricing

        All prices are per million tokens:

        | Model | Provider | Input | Cache write | Cache read | Output | Notes |
        | ----- | -------- | ----- | ----------- | ---------- | ------ | ----- |
        | [Composer 2.5](https://cursor.com) | Cursor | $0.5 | - | $0.2 | $2.5 | - |
        | [Claude 4.5 Sonnet](https://www.anthropic.com/claude/sonnet) | Anthropic | $3 | $3.75 | $0.3 | $15 | Hidden |
        | Grok 4.5 | Cursor | $2 | - | $0.5 | $6 | Jointly trained |

        ## Plans

        Pro plan details here.
        """;

    [Fact]
    public void ParseWithWarnings_reads_auto_and_model_table()
    {
        var (rates, warnings) = CursorDocsPricingMarkdownParser.ParseWithWarnings(SampleMarkdown);

        warnings.Should().BeEmpty();
        rates.Should().Contain(r => r.Model == "Auto");
        rates.Should().Contain(r => r.Model == "*");

        var auto = rates.Single(r => r.Model == "Auto");
        auto.InputPerMillion.Should().Be(1.25m);
        auto.OutputPerMillion.Should().Be(6.00m);
        auto.CacheReadPerMillion.Should().Be(0.25m);
        auto.CacheWritePerMillion.Should().Be(1.25m);

        var composer = rates.Single(r => r.Model == "Composer 2.5");
        composer.InputPerMillion.Should().Be(0.5m);
        composer.CacheWritePerMillion.Should().Be(0.5m); // dash falls back to input
        composer.CacheReadPerMillion.Should().Be(0.2m);
        composer.OutputPerMillion.Should().Be(2.5m);

        var sonnet = rates.Single(r => r.Model == "Claude 4.5 Sonnet");
        sonnet.InputPerMillion.Should().Be(3m);
        sonnet.CacheWritePerMillion.Should().Be(3.75m);
        sonnet.CacheReadPerMillion.Should().Be(0.3m);
        sonnet.OutputPerMillion.Should().Be(15m);

        rates.Should().Contain(r => r.Model == "Grok 4.5" && r.InputPerMillion == 2m);
    }

    [Fact]
    public void ResolveRate_matches_usage_slug_to_docs_display_name()
    {
        var rates = CursorDocsPricingMarkdownParser.Parse(SampleMarkdown);
        var match = CursorTokenCostCalculator.ResolveRate(rates, "composer-2.5-fast");
        match!.Model.Should().Be("Composer 2.5");
    }
}
