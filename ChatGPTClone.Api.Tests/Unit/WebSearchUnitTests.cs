using System.Net;
using System.Text;
using System.Text.Json;
using ChatGPTClone.Api.Configuration;
using ChatGPTClone.Api.Services.Web;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ChatGPTClone.Api.Tests.Unit;

public class WebSearchUnitTests
{
    private sealed class FakeTavily(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static TavilyWebSearchProvider Provider(HttpMessageHandler handler, string? key = "tvly-test-key") =>
        new(new HttpClient(handler), Options.Create(new WebSearchOptions { ApiKey = key, MaxResults = 3, MaxCharactersPerResult = 50 }),
            NullLogger<TavilyWebSearchProvider>.Instance);

    [Fact]
    public async Task Tavily_is_called_with_a_bearer_key_and_results_are_cleaned()
    {
        const string body = """
            {"results":[
              {"title":"Good","url":"https://example.com/a","content":"Some   text\nacross lines that is definitely longer than fifty characters in total."},
              {"title":"Script","url":"javascript:alert(1)","content":"x"},
              {"title":"","url":"http://plain.example/","content":"ok"}
            ]}
            """;
        var handler = new FakeTavily(HttpStatusCode.OK, body);

        var results = await Provider(handler).SearchAsync("launch date", CancellationToken.None);

        Assert.Equal("Bearer", handler.Request!.Headers.Authorization!.Scheme);
        Assert.Equal("tvly-test-key", handler.Request.Headers.Authorization.Parameter);
        var sent = JsonDocument.Parse(handler.RequestBody!).RootElement;
        Assert.Equal("launch date", sent.GetProperty("query").GetString());
        Assert.Equal(3, sent.GetProperty("max_results").GetInt32());
        Assert.DoesNotContain("tvly-test-key", handler.RequestBody); // the key is only in the header

        Assert.Equal(["https://example.com/a", "http://plain.example/"], results.Select(r => r.Url)); // javascript: dropped
        Assert.Equal(51, results[0].Content.Length); // 50 characters + "…"
        Assert.DoesNotContain("\n", results[0].Content);
        Assert.Equal("plain.example", results[1].Title); // no title: the host
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Api_errors_become_WebSearchException(HttpStatusCode status)
    {
        var ex = await Assert.ThrowsAsync<WebSearchException>(() =>
            Provider(new FakeTavily(status, """{"detail":"tvly-test-key is invalid"}""")).SearchAsync("q", CancellationToken.None));
        Assert.Contains(((int)status).ToString(), ex.Message);
        Assert.DoesNotContain("tvly", ex.Message);
    }

    [Fact]
    public async Task Without_a_key_nothing_is_sent()
    {
        var handler = new FakeTavily(HttpStatusCode.OK, "{}");
        var provider = Provider(handler, key: null);

        Assert.False(provider.IsConfigured);
        await Assert.ThrowsAsync<WebSearchException>(() => provider.SearchAsync("q", CancellationToken.None));
        Assert.Null(handler.Request);
    }

    [Fact]
    public void Citations_link_only_to_given_results_and_markdown_stays_safe()
    {
        IReadOnlyList<WebResult> results =
        [
            new("A", "https://www.a.example/path_(1)", "x"),
            new("B", "https://b.example/", "y"),
        ];

        Assert.Equal("Fact ([a.example](<https://www.a.example/path_(1)>), [b.example](<https://b.example/>)).",
            WebResults.LinkCitations("Fact [web:1, web:2].", results));
        Assert.Equal("Made up.", WebResults.LinkCitations("Made up [web:3].", results));
        Assert.Equal("No markers.", WebResults.LinkCitations("No markers.", results));
        Assert.Equal("Kept [file:12].", WebResults.LinkCitations("Kept [file:12].", results));
    }

    [Fact]
    public void Titles_and_urls_are_escaped_in_the_block()
    {
        var block = WebResults.Block([new WebResult("A \"quoted\" <b>title</b>", "https://x.example/?a=1&b=2", "text")], new DateTime(2026, 10, 26));

        Assert.Contains("title=\"A &quot;quoted&quot; &lt;b&gt;title&lt;/b&gt;\"", block);
        Assert.Contains("url=\"https://x.example/?a=1&amp;b=2\"", block);
        Assert.Contains("Today's date is 2026-10-26.", block);
    }
}
