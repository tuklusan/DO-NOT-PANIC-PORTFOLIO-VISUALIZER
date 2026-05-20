using System.Net;
using System.Net.Http;
using System.Text;
using PortfolioSaver.Core.Constants;
using PortfolioSaver.Core.Enums;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Screensaver.Services;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class FinanceNewsServiceTests
{
    [Fact]
    public async Task GetHeadlinesAsync_SummarizedMode_UsesRestylingOnlyPrompt_AndCachesAtFifteenMinuteFloor()
    {
        string cachePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "finance-news-cache.json");
        int requestCount = 0;
        string? capturedBody = null;
        FakeHttpMessageHandler handler = new(request =>
        {
            string requestUrl = request.RequestUri?.ToString() ?? string.Empty;
            if (requestUrl == "https://www.cnbc.com/id/19832390/device/rss/rss.html")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    <rss><channel>
                      <item><title>Oil prices fall after Iran sends updated peace proposal to mediators in Pakistan</title></item>
                      <item><title>Australia and Japan markets climb, looking past Iran war escalation fears</title></item>
                    </channel></rss>
                    """, Encoding.UTF8, "application/xml")
                };
            }

            if (requestUrl == "https://feeds.bbci.co.uk/news/business/rss.xml")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    <rss><channel>
                      <item><title>Airlines can cancel flights in advance over fuel shortages under new plans</title></item>
                      <item><title>In five charts: How UAE's exit could affect Opec's influence over the oil price</title></item>
                    </channel></rss>
                    """, Encoding.UTF8, "application/xml")
                };
            }

            if (requestUrl == "https://rss.nytimes.com/services/xml/rss/nyt/Economy.xml")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    <rss><channel>
                      <item><title>Fed Officials Cite Inflation Concerns in Defending Dissents</title></item>
                    </channel></rss>
                    """, Encoding.UTF8, "application/xml")
                };
            }

            requestCount++;
            capturedBody = request.Content is null ? string.Empty : request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            Assert.Equal("https://api.deepseek.com/chat/completions", requestUrl);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("test-deepseek-key", request.Headers.Authorization?.Parameter);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "choices": [
                    {
                      "message": {
                        "content": "Global stocks were mixed as traders weighed labor data, central-bank caution, and softer energy sentiment across regions."
                      }
                    }
                  ]
                }
                """, Encoding.UTF8, "application/json")
            };
        });

        using HttpClient client = new(handler);
        FinanceNewsService service = new(cachePath, () => string.Empty);
        AppSettings settings = new()
        {
            NewsScrollerMode = NewsScrollerMode.SummarizedFinancialNews,
            DeepSeekWritingStyle = DeepSeekWritingStyle.DouglasAdams,
            NewsFeedUrl = Defaults.DefaultNewsFeedUrl,
            NewsRefreshMinutes = 5,
            DeepSeekApiKey = "test-deepseek-key"
        };

        IReadOnlyList<string> first = await service.GetHeadlinesAsync(
            client,
            settings,
            networkAvailable: true);

        IReadOnlyList<string> second = await service.GetHeadlinesAsync(
            client,
            settings,
            networkAvailable: true);

        Assert.Equal(2, first.Count);
        Assert.Equal(first, second);
        Assert.Equal(1, requestCount);
        Assert.Contains("You are a dependable fiduciary and are presenting current financial news highlights to your customers.", capturedBody, StringComparison.Ordinal);
        Assert.Contains("You write in the style of Douglas Adams.", capturedBody, StringComparison.Ordinal);
        Assert.Contains("Oil prices fall after Iran sends updated peace proposal to mediators in Pakistan", capturedBody, StringComparison.Ordinal);
        Assert.Contains("Fed Officials Cite Inflation Concerns in Defending Dissents", capturedBody, StringComparison.Ordinal);
        Assert.Contains("Only restyle the supplied facts into a cohesive paragraph.", capturedBody, StringComparison.Ordinal);
        Assert.Contains("Never include investment recommendations", capturedBody, StringComparison.Ordinal);
        Assert.Contains("Do not include any specific numerical values, prices, percentages, dates, or times", capturedBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Closing quotation:", capturedBody, StringComparison.Ordinal);
        Assert.DoesNotContain("79.61", capturedBody, StringComparison.Ordinal);
        Assert.DoesNotContain("79.61", first[0], StringComparison.Ordinal);
        Assert.DoesNotContain(Environment.NewLine, first[0], StringComparison.Ordinal);
        Assert.Equal("Global stocks were mixed as traders weighed labor data, central-bank caution, and softer energy sentiment across regions.", first[0]);
        Assert.Equal("[[CLOSING_QUOTE]] \"Nothing travels faster than the speed of light, with the possible exception of bad news, which obeys its own special laws.\"", first[1]);
    }

    [Fact]
    public async Task GetHeadlinesAsync_SummarizedMode_UsesResolverKeyWhenExplicitKeyMissing()
    {
        string cachePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "finance-news-cache.json");
        string? capturedAuthorization = null;
        FakeHttpMessageHandler handler = new(request =>
        {
            string requestUrl = request.RequestUri?.ToString() ?? string.Empty;
            if (requestUrl == "https://www.cnbc.com/id/19832390/device/rss/rss.html" ||
                requestUrl == "https://feeds.bbci.co.uk/news/business/rss.xml" ||
                requestUrl == "https://rss.nytimes.com/services/xml/rss/nyt/Economy.xml")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    <rss><channel><item><title>Bond yields steady as growth worries linger</title></item></channel></rss>
                    """, Encoding.UTF8, "application/xml")
                };
            }

            capturedAuthorization = request.Headers.Authorization?.Parameter;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "choices": [
                    {
                      "message": {
                        "content": "Markets traded sideways as investors weighed soft manufacturing data against resilient labor figures and steady energy prices."
                      }
                    }
                  ]
                }
                """, Encoding.UTF8, "application/json")
            };
        });

        using HttpClient client = new(handler);
        FinanceNewsService service = new(cachePath, () => "resolver-deepseek-key");
        AppSettings settings = new()
        {
            NewsScrollerMode = NewsScrollerMode.SummarizedFinancialNews,
            DeepSeekWritingStyle = DeepSeekWritingStyle.DouglasAdams,
            NewsFeedUrl = Defaults.DefaultNewsFeedUrl,
            NewsRefreshMinutes = 15,
            DeepSeekApiKey = string.Empty
        };

        IReadOnlyList<string> headlines = await service.GetHeadlinesAsync(
            client,
            settings,
            networkAvailable: true);

        Assert.Equal(2, headlines.Count);
        Assert.Equal("resolver-deepseek-key", capturedAuthorization);
    }

    [Fact]
    public async Task GetHeadlinesAsync_SummarizedMode_ShakespeareAppendsExactClosingQuotation()
    {
        string cachePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "finance-news-cache.json");
        string? capturedBody = null;
        FakeHttpMessageHandler handler = new(request =>
        {
            string requestUrl = request.RequestUri?.ToString() ?? string.Empty;
            if (requestUrl == "https://www.cnbc.com/id/19832390/device/rss/rss.html" ||
                requestUrl == "https://feeds.bbci.co.uk/news/business/rss.xml" ||
                requestUrl == "https://rss.nytimes.com/services/xml/rss/nyt/Economy.xml")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    <rss><channel><item><title>Bond yields steady as growth worries linger</title></item></channel></rss>
                    """, Encoding.UTF8, "application/xml")
                };
            }

            capturedBody = request.Content is null ? string.Empty : request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "choices": [
                    {
                      "message": {
                        "content": "Markets grew cautious as policymakers weighed growth against stubborn price pressures"
                      }
                    }
                  ]
                }
                """, Encoding.UTF8, "application/json")
            };
        });

        using HttpClient client = new(handler);
        FinanceNewsService service = new(cachePath, () => string.Empty);
        AppSettings settings = new()
        {
            NewsScrollerMode = NewsScrollerMode.SummarizedFinancialNews,
            DeepSeekWritingStyle = DeepSeekWritingStyle.WilliamShakespeare,
            NewsRefreshMinutes = 15,
            DeepSeekApiKey = "test-deepseek-key"
        };

        IReadOnlyList<string> headlines = await service.GetHeadlinesAsync(client, settings, networkAvailable: true);

        Assert.Equal(2, headlines.Count);
        Assert.Contains("You write in the style of William Shakespeare.", capturedBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Closing quotation:", capturedBody, StringComparison.Ordinal);
        Assert.Equal("[[CLOSING_QUOTE]] \"All that glisters is not gold.\"", headlines[1]);
    }

    [Fact]
    public async Task GetHeadlinesAsync_SummarizedMode_WithoutApiKey_UsesSummaryFallback()
    {
        string cachePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "finance-news-cache.json");
        using HttpClient client = new(new FakeHttpMessageHandler(_ => throw new InvalidOperationException("HTTP should not be used without an API key.")));
        FinanceNewsService service = new(cachePath, () => string.Empty);
        AppSettings settings = new()
        {
            NewsScrollerMode = NewsScrollerMode.SummarizedFinancialNews,
            DeepSeekWritingStyle = DeepSeekWritingStyle.DouglasAdams,
            NewsFeedUrl = Defaults.DefaultNewsFeedUrl,
            NewsRefreshMinutes = 15,
            DeepSeekApiKey = string.Empty
        };

        IReadOnlyList<string> headlines = await service.GetHeadlinesAsync(
            client,
            settings,
            networkAvailable: true);

        Assert.Contains(headlines, headline => headline.Contains("Waiting for summarized financial news", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }
}
