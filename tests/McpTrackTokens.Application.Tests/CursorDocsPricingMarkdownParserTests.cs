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

    private const string CurrentDocsMarkdown = """
        # Models & Pricing

        ## API pool

        ### Model pricing

        All prices are per million tokens:

        | Model | Provider | Input | Cache write | Cache read | Output | Notes |
        | ----- | -------- | ----- | ----------- | ---------- | ------ | ----- |
        | Auto Cost | Cursor | $1.25 | $1.25 | $0.25 | $6 | Hidden by default |
        | [Composer 2.5](https://cursor.com) | Cursor | $0.5 | - | $0.2 | $2.5 | - |

        ## Plans
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
    public void ParseWithWarnings_reads_cursor_models_table_before_other_models()
    {
        const string markdown = """
            # Models & Pricing

            ## Cursor Models

            | Model | Provider | Input | Cache write | Cache read | Output | Notes |
            | ----- | -------- | ----- | ----------- | ---------- | ------ | ----- |
            | Grok 4.6 | Cursor | $2 | - | $0.5 | $6 | Jointly trained |
            | [Composer 2.5](https://cursor.com/blog/composer-2-5) | Cursor | $0.5 | - | $0.2 | $2.5 | - |

            ## Other Models

            ### Model pricing

            | Model | Provider | Input | Cache write | Cache read | Output | Notes |
            | ----- | -------- | ----- | ----------- | ---------- | ------ | ----- |
            | [Claude 4.6 Sonnet](https://www.anthropic.com/claude/sonnet) | Anthropic | $3 | $3.75 | $0.3 | $15 | Hidden |

            ## Auto modes

            ### Auto Cost

            Auto Cost pricing is set per million tokens, regardless of which model is used.
            """;

        var (rates, warnings) = CursorDocsPricingMarkdownParser.ParseWithWarnings(markdown);

        warnings.Should().ContainSingle(w => w.Contains("Auto pricing was not listed"));
        rates.Should().Contain(r => r.Model == "Grok 4.6" && r.InputPerMillion == 2m && r.CacheReadPerMillion == 0.5m);
        rates.Should().Contain(r => r.Model == "Composer 2.5" && r.InputPerMillion == 0.5m);
        rates.Should().Contain(r => r.Model == "Claude 4.6 Sonnet" && r.OutputPerMillion == 15m);
        rates.Should().Contain(r => r.Model == "Auto" && r.InputPerMillion == 1.25m);
        rates.Should().Contain(r => r.Model == "*");
    }

    [Fact]
    public void ParseWithWarnings_maps_auto_cost_row_from_current_docs()
    {
        var (rates, warnings) = CursorDocsPricingMarkdownParser.ParseWithWarnings(CurrentDocsMarkdown);

        warnings.Should().BeEmpty();
        rates.Should().Contain(r => r.Model == "Auto");
        rates.Should().Contain(r => r.Model == "*");
        rates.Should().NotContain(r => r.Model == "Auto Cost");

        var auto = rates.Single(r => r.Model == "Auto");
        auto.InputPerMillion.Should().Be(1.25m);
        auto.OutputPerMillion.Should().Be(6m);
        auto.CacheReadPerMillion.Should().Be(0.25m);
        auto.CacheWritePerMillion.Should().Be(1.25m);

        var fallback = rates.Single(r => r.Model == "*");
        fallback.InputPerMillion.Should().Be(auto.InputPerMillion);
        fallback.OutputPerMillion.Should().Be(auto.OutputPerMillion);
    }

    [Fact]
    public void ParseWithWarnings_includes_cursor_models_from_live_docs_fixture()
    {
        var markdown = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "models-and-pricing.md"));

        var (rates, _) = CursorDocsPricingMarkdownParser.ParseWithWarnings(markdown);

        rates.Should().Contain(r => r.Model == "Grok 4.6" && r.InputPerMillion == 2m && r.OutputPerMillion == 6m);
        rates.Should().Contain(r => r.Model == "Grok 4.6 (Fast)" && r.InputPerMillion == 4m);
        rates.Should().Contain(r => r.Model == "Grok 4.5" && r.CacheReadPerMillion == 0.5m);
        rates.Should().Contain(r => r.Model == "Grok 4.5 (Fast)" && r.OutputPerMillion == 12m);
        rates.Should().Contain(r => r.Model == "Composer 2.5" && r.InputPerMillion == 0.5m && r.OutputPerMillion == 2.5m);
        rates.Should().Contain(r => r.Model == "Composer 2.5 (Fast)" && r.InputPerMillion == 3m && r.OutputPerMillion == 15m);
        CursorDocsPricingMarkdownParser.HasCursorPoolRates(rates).Should().BeTrue();
    }

    [Fact]
    public void ParseWithWarnings_reports_no_cursor_pool_when_only_other_models_exist()
    {
        const string markdown = """
            # Models & Pricing

            ### Model pricing

            | Model | Provider | Input | Cache write | Cache read | Output | Notes |
            | ----- | -------- | ----- | ----------- | ---------- | ------ | ----- |
            | [Claude 4.6 Sonnet](https://www.anthropic.com/claude/sonnet) | Anthropic | $3 | $3.75 | $0.3 | $15 | Hidden |
            """;

        var (rates, _) = CursorDocsPricingMarkdownParser.ParseWithWarnings(markdown);

        rates.Should().Contain(r => r.Model == "Claude 4.6 Sonnet");
        CursorDocsPricingMarkdownParser.HasCursorPoolRates(rates).Should().BeFalse();
    }

    [Fact]
    public void ResolveRate_matches_usage_slug_to_docs_display_name()
    {
        var rates = CursorDocsPricingMarkdownParser.Parse(SampleMarkdown);
        var match = CursorTokenCostCalculator.ResolveRate(rates, "composer-2.5-fast");
        match!.Model.Should().Be("Composer 2.5");
    }
}
