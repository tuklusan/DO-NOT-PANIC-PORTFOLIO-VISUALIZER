using System.Net;
using System.Net.Http;
using System.Text;
using PortfolioSaver.Core.Constants;
using PortfolioSaver.Core.Enums;
using PortfolioSaver.Screensaver.Services;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class FinanceNewsServiceTests
{
    [Fact]
    public async Task GetHeadlinesAsync_SummarizedMode_UsesDeepSeekPrompt_AndCachesAtFifteenMinuteFloor()
    {
        string cachePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "finance-news-cache.json");
        int requestCount = 0;
        string? capturedBody = null;
        FakeHttpMessageHandler handler = new(request =>
        {
            requestCount++;
            capturedBody = request.Content is null
                ? string.Empty
                : request.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            Assert.Equal("https://api.deepseek.com/chat/completions", request.RequestUri?.ToString());
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("test-deepseek-key", request.Headers.Authorization?.Parameter);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "choices": [
                    {
                      "message": {
                        "content": "Stocks rose overnight as investors weighed central-bank guidance,\ncredit conditions, and mixed regional data while commodities stayed volatile."
                      }
                    }
                  ]
                }
                """, Encoding.UTF8, "application/json")
            };
        });

        using HttpClient client = new(handler);
        FinanceNewsService service = new(cachePath, () => "test-deepseek-key");

        IReadOnlyList<string> first = await service.GetHeadlinesAsync(
            client,
            NewsScrollerMode.SummarizedFinancialNews,
            "test-deepseek-key",
            Defaults.DefaultNewsFeedUrl,
            refreshMinutes: 5,
            networkAvailable: true);

        IReadOnlyList<string> second = await service.GetHeadlinesAsync(
            client,
            NewsScrollerMode.SummarizedFinancialNews,
            "test-deepseek-key",
            Defaults.DefaultNewsFeedUrl,
            refreshMinutes: 5,
            networkAvailable: true);

        Assert.Single(first);
        Assert.Equal(first[0], second[0]);
        Assert.Equal(1, requestCount);
        Assert.Contains("Enable Web Search and Summarize the latest global financial news in one paragraph", capturedBody, StringComparison.Ordinal);
        Assert.DoesNotContain(Environment.NewLine, first[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetHeadlinesAsync_SummarizedMode_WithoutApiKey_UsesSummaryFallback()
    {
        string cachePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "finance-news-cache.json");
        using HttpClient client = new(new FakeHttpMessageHandler(_ => throw new InvalidOperationException("HTTP should not be used without an API key.")));
        FinanceNewsService service = new(cachePath, () => string.Empty);

        IReadOnlyList<string> headlines = await service.GetHeadlinesAsync(
            client,
            NewsScrollerMode.SummarizedFinancialNews,
            string.Empty,
            Defaults.DefaultNewsFeedUrl,
            refreshMinutes: 15,
            networkAvailable: true);

        Assert.Contains(headlines, headline => headline.Contains("Waiting for summarized financial news", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }
}
