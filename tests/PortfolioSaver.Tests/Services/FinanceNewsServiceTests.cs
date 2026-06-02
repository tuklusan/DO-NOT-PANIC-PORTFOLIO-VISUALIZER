using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using PortfolioSaver.Core.Constants;
using PortfolioSaver.Core.Enums;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Screensaver.Services;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class FinanceNewsServiceTests
{
    private static readonly JsonSerializerOptions CacheJsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

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
            Assert.Equal("https://api.deepseek.com/v1/chat/completions", requestUrl);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("test-deepseek-key", request.Headers.Authorization?.Parameter);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "choices": [
                    {
                      "message": {
                        "content": "[[ITEM]]\nPaperwork storms gather.\nMarkets shuffle through the fog.\nClerks misplace their calm.\n---\nGlobal stocks were mixed as traders weighed labor data, central-bank caution, and softer energy sentiment across regions.\n[[/ITEM]]"
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
            DeepSeekApiKey = "test-deepseek-key",
            DeepSeekEndpointUrl = Defaults.DefaultDeepSeekEndpointUrl,
            DeepSeekModelId = Defaults.DefaultDeepSeekModelId
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
        Assert.Contains("[[ITEM]]", capturedBody, StringComparison.Ordinal);
        Assert.Contains("The haiku may sound bleak, officious, or absurdly bureaucratic in a Vogon-adjacent way", capturedBody, StringComparison.Ordinal);
        Assert.Contains("Oil prices fall after Iran sends updated peace proposal to mediators in Pakistan", capturedBody, StringComparison.Ordinal);
        Assert.Contains("Fed Officials Cite Inflation Concerns in Defending Dissents", capturedBody, StringComparison.Ordinal);
        Assert.Contains("Only restyle the supplied facts.", capturedBody, StringComparison.Ordinal);
        Assert.Contains("Never include investment recommendations", capturedBody, StringComparison.Ordinal);
        Assert.Contains("Do not include any specific numerical values, prices, percentages, dates, or times unless the source headline itself makes the number essential", capturedBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Closing quotation:", capturedBody, StringComparison.Ordinal);
        Assert.DoesNotContain("79.61", capturedBody, StringComparison.Ordinal);
        Assert.DoesNotContain("79.61", first[0], StringComparison.Ordinal);
        Assert.Equal(
            "Paperwork storms gather." + Environment.NewLine +
            "Markets shuffle through the fog." + Environment.NewLine +
            "Clerks misplace their calm." + Environment.NewLine +
            "Global stocks were mixed as traders weighed labor data, central-bank caution, and softer energy sentiment across regions.",
            first[0]);
        Assert.Equal("[[CLOSING_QUOTE]] \"Nothing travels faster than the speed of light, with the possible exception of bad news, which obeys its own special laws.\"", first[1]);
    }

    [Fact]
    public async Task GetHeadlinesAsync_SummarizedMode_UsesConfiguredEndpointAndModel()
    {
        string cachePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "finance-news-cache.json");
        string? capturedBody = null;
        string? capturedRequestUrl = null;
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
                    <rss><channel><item><title>Dollar holds steady as commodity markets regroup</title></item></channel></rss>
                    """, Encoding.UTF8, "application/xml")
                };
            }

            capturedRequestUrl = requestUrl;
            capturedBody = request.Content is null ? string.Empty : request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "choices": [
                    {
                      "message": {
                        "content": "[[ITEM]]\nSatchels mark the forms.\nCopper sighs in patient fog.\nClerks await the bell.\n---\nCurrencies and commodities steadied as traders reassessed growth fears against calmer funding markets.\n[[/ITEM]]"
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
            NewsRefreshMinutes = 15,
            DeepSeekApiKey = "test-deepseek-key",
            DeepSeekEndpointUrl = "https://localhost:11434/v1/chat/completions",
            DeepSeekModelId = "llama3"
        };

        IReadOnlyList<string> headlines = await service.GetHeadlinesAsync(client, settings, networkAvailable: true);

        Assert.Equal(2, headlines.Count);
        Assert.Equal("https://localhost:11434/v1/chat/completions", capturedRequestUrl);
        Assert.Contains("\"model\":\"llama3\"", capturedBody, StringComparison.Ordinal);
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
                        "content": "[[ITEM]]\nLedgers hum at dusk.\nFactories cough in the haze.\nTraders sip bad tea.\n---\nMarkets traded sideways as investors weighed soft manufacturing data against resilient labor figures and steady energy prices.\n[[/ITEM]]"
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
                        "content": "[[ITEM]]\nVelvet curtains shake.\nCouncils mutter into dust.\nRates haunt the antechamber.\n---\nMarkets grew cautious as policymakers weighed growth against stubborn price pressures.\n[[/ITEM]]"
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
        Assert.Contains("You write in the style of classical Shakespeare.", capturedBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Closing quotation:", capturedBody, StringComparison.Ordinal);
        Assert.Equal("[[CLOSING_QUOTE]] \"All that glisters is not gold.\"", headlines[1]);
    }

    [Fact]
    public void ParseSummarizedNewsItems_ExtractsHaikuThenProsePerItem()
    {
        MethodInfo parseMethod = typeof(FinanceNewsService).GetMethod(
            "ParseSummarizedNewsItems",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("FinanceNewsService.ParseSummarizedNewsItems not found.");

        List<string> items = Assert.IsType<List<string>>(parseMethod.Invoke(null, ["""
        [[ITEM]]
        Clerks stamp the void.
        Bond markets cough into fog.
        Tea goes cold again.
        ---
        Bond markets drifted as traders weighed slower growth against stubborn inflation worries.
        [[/ITEM]]
        [[ITEM]]
        Cargo drones complain.
        Supply chains grumble at dusk.
        Someone lost the forms.
        ---
        Shipping shares steadied after ports resumed partial operations and fuel fears eased.
        [[/ITEM]]
        """]));

        Assert.Equal(2, items.Count);
        Assert.Equal(
            "Clerks stamp the void." + Environment.NewLine +
            "Bond markets cough into fog." + Environment.NewLine +
            "Tea goes cold again." + Environment.NewLine +
            "Bond markets drifted as traders weighed slower growth against stubborn inflation worries.",
            items[0]);
        Assert.Equal(
            "Cargo drones complain." + Environment.NewLine +
            "Supply chains grumble at dusk." + Environment.NewLine +
            "Someone lost the forms." + Environment.NewLine +
            "Shipping shares steadied after ports resumed partial operations and fuel fears eased.",
            items[1]);
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

    [Fact]
    public async Task GetHeadlinesAsync_SummarizedMode_CachesRssFallbackAfterDeepSeekFailure()
    {
        string cachePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "finance-news-cache.json");
        int rssRequestCount = 0;
        int deepSeekRequestCount = 0;
        FakeHttpMessageHandler handler = new(request =>
        {
            string requestUrl = request.RequestUri?.ToString() ?? string.Empty;
            if (requestUrl == "https://www.cnbc.com/id/19832390/device/rss/rss.html" ||
                requestUrl == "https://feeds.bbci.co.uk/news/business/rss.xml" ||
                requestUrl == "https://rss.nytimes.com/services/xml/rss/nyt/Economy.xml")
            {
                rssRequestCount++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    <rss><channel><item><title>Markets brace for a volatile week as oil and bonds diverge</title></item></channel></rss>
                    """, Encoding.UTF8, "application/xml")
                };
            }

            deepSeekRequestCount++;
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("service unavailable", Encoding.UTF8, "text/plain")
            };
        });

        using HttpClient client = new(handler);
        FinanceNewsService service = new(cachePath, () => string.Empty);
        AppSettings settings = new()
        {
            NewsScrollerMode = NewsScrollerMode.SummarizedFinancialNews,
            DeepSeekWritingStyle = DeepSeekWritingStyle.DouglasAdams,
            NewsFeedUrl = Defaults.DefaultNewsFeedUrl,
            NewsRefreshMinutes = 15,
            DeepSeekApiKey = "test-deepseek-key"
        };

        IReadOnlyList<string> first = await service.GetHeadlinesAsync(client, settings, networkAvailable: true);
        IReadOnlyList<string> second = await service.GetHeadlinesAsync(client, settings, networkAvailable: true);

        Assert.Single(first);
        Assert.Equal(first, second);
        Assert.Equal(3, rssRequestCount);
        Assert.Equal(1, deepSeekRequestCount);
        Assert.Contains("Markets brace for a volatile week as oil and bonds diverge", first[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetHeadlinesAsync_SummarizedMode_RetainsStyledCacheWhenRefreshFallsBack()
    {
        string cachePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "finance-news-cache.json");
        int deepSeekRequestCount = 0;
        bool failDeepSeek = false;
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
                    <rss><channel><item><title>Markets brace for a volatile week as oil and bonds diverge</title></item></channel></rss>
                    """, Encoding.UTF8, "application/xml")
                };
            }

            deepSeekRequestCount++;
            if (failDeepSeek)
            {
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new StringContent("service unavailable", Encoding.UTF8, "text/plain")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "choices": [
                    {
                      "message": {
                        "content": "[[ITEM]]\nPaperwork storms gather.\nMarkets shuffle through the fog.\nClerks misplace their calm.\n---\nGlobal stocks were mixed as traders weighed labor data, central-bank caution, and softer energy sentiment across regions.\n[[/ITEM]]"
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
            NewsRefreshMinutes = 15,
            DeepSeekApiKey = "test-deepseek-key"
        };

        IReadOnlyList<string> first = await service.GetHeadlinesAsync(client, settings, networkAvailable: true);
        NewsHeadlineCache cache = JsonSerializer.Deserialize<NewsHeadlineCache>(await File.ReadAllTextAsync(cachePath), CacheJsonOptions)
            ?? throw new InvalidOperationException("Cached news payload was not written.");
        cache.FetchTimestampUtc = DateTimeOffset.UtcNow.AddMinutes(-20);
        await File.WriteAllTextAsync(cachePath, JsonSerializer.Serialize(cache, CacheJsonOptions));

        failDeepSeek = true;
        IReadOnlyList<string> second = await service.GetHeadlinesAsync(client, settings, networkAvailable: true);

        Assert.Equal(2, deepSeekRequestCount);
        Assert.Equal(first, second);
        Assert.DoesNotContain(second, headline => headline.Contains("Markets brace for a volatile week as oil and bonds diverge", StringComparison.Ordinal));
    }

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }
}
