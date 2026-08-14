using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using McpTrackTokens.Infrastructure.Pricing;

namespace McpTrackTokens.Infrastructure.Tests;

public sealed class CursorDocsPricingClientTests
{
    private const string PricingMarkdown = """
        # Models & Pricing

        ## Cursor Models

        | Model | Provider | Input | Cache write | Cache read | Output | Notes |
        | ----- | -------- | ----- | ----------- | ---------- | ------ | ----- |
        | Grok 4.6 | Cursor | $2 | - | $0.5 | $6 | Jointly trained |
        | Composer 2.5 | Cursor | $0.5 | - | $0.2 | $2.5 | - |

        ### Model pricing

        | Model | Provider | Input | Cache write | Cache read | Output | Notes |
        | ----- | -------- | ----- | ----------- | ---------- | ------ | ----- |
        | Auto Cost | Cursor | $1.25 | $1.25 | $0.25 | $6 | Hidden |
        | Claude 4.6 Sonnet | Anthropic | $3 | $3.75 | $0.3 | $15 | Hidden |
        """;

    [Fact]
    public async Task FetchRatesAsync_does_not_send_markdown_accept_header()
    {
        var handler = new RecordingHandler
        {
            Body = PricingMarkdown,
            StatusCode = HttpStatusCode.OK
        };
        using var http = new HttpClient(handler);
        var client = new CursorDocsPricingClient(http, NullLogger<CursorDocsPricingClient>.Instance);

        var result = await client.FetchRatesAsync();

        handler.Requests.Should().NotBeEmpty();
        handler.Requests.Should().OnlyContain(request =>
            request.Headers.Accept.All(value => value.MediaType == "*/*"));
        handler.Requests.Should().NotContain(request =>
            request.Headers.Accept.Any(value =>
                string.Equals(value.MediaType, "text/markdown", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value.MediaType, "text/plain", StringComparison.OrdinalIgnoreCase)));
        result.Count.Should().BeGreaterThan(0);
        result.Rates.Should().Contain(r => r.Model == "Grok 4.6");
        result.Rates.Should().Contain(r => r.Model == "Composer 2.5");
        result.Rates.Should().Contain(r => r.Model == "Claude 4.6 Sonnet");
    }

    [Fact]
    public async Task FetchRatesAsync_succeeds_when_markdown_accept_would_404()
    {
        var handler = new RecordingHandler
        {
            Body = PricingMarkdown,
            StatusCode = HttpStatusCode.OK,
            NotFoundWhenAcceptIncludesMarkdown = true
        };
        using var http = new HttpClient(handler);
        var client = new CursorDocsPricingClient(http, NullLogger<CursorDocsPricingClient>.Instance);

        var result = await client.FetchRatesAsync();

        result.Rates.Should().Contain(r => r.Model == "Auto");
        result.Rates.Should().Contain(r => r.Model == "Grok 4.6");
        result.Rates.Should().Contain(r => r.Model == "Composer 2.5");
    }

    [Fact]
    public async Task FetchRatesAsync_throws_when_cursor_models_table_is_missing()
    {
        const string otherModelsOnly = """
            # Models & Pricing

            ### Model pricing

            | Model | Provider | Input | Cache write | Cache read | Output | Notes |
            | ----- | -------- | ----- | ----------- | ---------- | ------ | ----- |
            | Claude 4.6 Sonnet | Anthropic | $3 | $3.75 | $0.3 | $15 | Hidden |
            """;

        var handler = new RecordingHandler
        {
            Body = otherModelsOnly,
            StatusCode = HttpStatusCode.OK
        };
        using var http = new HttpClient(handler);
        var client = new CursorDocsPricingClient(http, NullLogger<CursorDocsPricingClient>.Instance);

        var act = async () => await client.FetchRatesAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Cursor Models table was missing*");
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;

        public string Body { get; set; } = string.Empty;

        public bool NotFoundWhenAcceptIncludesMarkdown { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(CloneHeaders(request));

            var acceptMarkdown = request.Headers.Accept.Any(value =>
                string.Equals(value.MediaType, "text/markdown", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value.MediaType, "text/plain", StringComparison.OrdinalIgnoreCase));
            var status = NotFoundWhenAcceptIncludesMarkdown && acceptMarkdown
                ? HttpStatusCode.NotFound
                : StatusCode;

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(status == HttpStatusCode.OK ? Body : "Not Found")
            });
        }

        private static HttpRequestMessage CloneHeaders(HttpRequestMessage request)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return clone;
        }
    }
}
