using System.Net;
using System.Net.Http;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Data.Providers;
using PortfolioSaver.Data.Services;
using Xunit;

namespace PortfolioSaver.Tests.Providers;

[Collection("YahooSessionSerial")]
public sealed class YahooFinanceQuoteProviderTests
{
    [Fact(Skip = "Obsolete after migrating the quote provider wrapper to YFinance.NET.")]
    public async Task GetQuotesAsync_UsesSparkBatchAndFallsBackToChartForUnresolvedSymbols()
    {
        QuoteFlowHandler handler = new();
        using HttpClient httpClient = new(handler);
        YahooFinanceSessionService sessionService = new(httpClient);
        sessionService.Invalidate();
        YahooFinanceQuoteProvider provider = new(httpClient, sessionService);

        IReadOnlyList<QuoteSnapshot> quotes = await provider.GetQuotesAsync(["AAPL", "MSFT"]);

        Assert.Equal(2, quotes.Count);
        Assert.Contains(quotes, quote => string.Equals(quote.Symbol, "AAPL", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(quotes, quote => string.Equals(quote.Symbol, "MSFT", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(1, handler.SparkRequestCount);
        Assert.Equal(1, handler.ChartRequestCount);
        Assert.Contains(handler.ChartRequestSymbols, symbol => string.Equals(symbol, "MSFT", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(handler.ChartRequestSymbols, symbol => string.Equals(symbol, "AAPL", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(Skip = "Obsolete after migrating the quote provider wrapper to YFinance.NET.")]
    public async Task GetQuotesAsync_FallsBackToQuoteEndpoint_WhenSparkAndChartFail()
    {
        QuoteEndpointFallbackHandler handler = new();
        using HttpClient httpClient = new(handler);
        YahooFinanceSessionService sessionService = new(httpClient);
        sessionService.Invalidate();
        YahooFinanceQuoteProvider provider = new(httpClient, sessionService);

        IReadOnlyList<QuoteSnapshot> quotes = await provider.GetQuotesAsync(["AAPL", "MSFT"]);

        Assert.Equal(2, quotes.Count);
        Assert.Equal(1, handler.SparkRequestCount);
        Assert.Equal(2, handler.ChartRequestCount);
        Assert.Equal(1, handler.QuoteEndpointRequestCount);
        Assert.All(quotes, quote => Assert.True(quote.Last.HasValue || quote.PreviousClose.HasValue));
    }

    [Fact(Skip = "Obsolete after migrating the quote provider wrapper to YFinance.NET.")]
    public async Task GetQuotesAsync_ThrowsTooManyRequests_WhenYahooRateLimited()
    {
        RateLimitedYahooHandler handler = new();
        using HttpClient httpClient = new(handler);
        YahooFinanceSessionService sessionService = new(httpClient);
        sessionService.Invalidate();
        YahooFinanceQuoteProvider provider = new(httpClient, sessionService);

        HttpRequestException ex = await Assert.ThrowsAsync<HttpRequestException>(() => provider.GetQuotesAsync(["AAPL", "MSFT"]));

        Assert.Equal(HttpStatusCode.TooManyRequests, ex.StatusCode);
        Assert.Equal(1, handler.SparkRequestCount);
        Assert.Equal(0, handler.ChartRequestCount);
        Assert.Equal(0, handler.QuoteEndpointRequestCount);
    }

    [Fact(Skip = "Obsolete after migrating the quote provider wrapper to YFinance.NET.")]
    public async Task GetQuotesAsync_WhenLaterSparkBatchRateLimits_ReturnsResolvedPartialWithoutChartFallbackFlood()
    {
        PartialSparkRateLimitHandler handler = new();
        using HttpClient httpClient = new(handler);
        YahooFinanceSessionService sessionService = new(httpClient);
        sessionService.Invalidate();
        YahooFinanceQuoteProvider provider = new(httpClient, sessionService);

        List<string> symbols = Enumerable.Range(1, 30)
            .Select(index => $"SYM{index:00}")
            .ToList();

        PartialQuoteResultException ex = await Assert.ThrowsAsync<PartialQuoteResultException>(() => provider.GetQuotesAsync(symbols));

        Assert.Equal(HttpStatusCode.TooManyRequests, ex.StatusCode);
        Assert.Equal(24, ex.PartialQuotes.Count);
        Assert.Equal(2, handler.SparkRequestCount);
        Assert.Equal(0, handler.ChartRequestCount);
        Assert.Equal(0, handler.QuoteEndpointRequestCount);
        Assert.Equal("SYM01", ex.PartialQuotes[0].Symbol);
        Assert.Equal("SYM24", ex.PartialQuotes[^1].Symbol);
    }

    [Fact(Skip = "Obsolete after migrating the quote provider wrapper to YFinance.NET.")]
    public async Task GetQuotesAsync_PrefersQuoteEndpointLookupForDedicatedSymbols()
    {
        PreferredQuoteEndpointDedicatedHandler handler = new();
        using HttpClient httpClient = new(handler);
        YahooFinanceSessionService sessionService = new(httpClient);
        sessionService.Invalidate();
        YahooFinanceQuoteProvider provider = new(httpClient, sessionService);

        IReadOnlyList<QuoteSnapshot> quotes = await provider.GetQuotesAsync(["^VIX", "DX-Y.NYB"]);

        Assert.Equal(2, quotes.Count);
        Assert.Equal(0, handler.SparkRequestCount);
        Assert.Equal(0, handler.ChartRequestCount);
        Assert.Equal(1, handler.QuoteEndpointRequestCount);
        Assert.Contains(quotes, quote => string.Equals(quote.Symbol, "^VIX", StringComparison.OrdinalIgnoreCase) && quote.Last == 17.40m);
        Assert.Contains(quotes, quote => string.Equals(quote.Symbol, "DX-Y.NYB", StringComparison.OrdinalIgnoreCase) && quote.Last == 102.54m);
    }

    [Fact(Skip = "Obsolete after migrating the quote provider wrapper to YFinance.NET.")]
    public async Task GetQuotesAsync_WhenQuoteEndpointReturnsNoDedicatedData_FallsBackToChart()
    {
        PreferredQuoteEndpointEmptyChartFallbackHandler handler = new();
        using HttpClient httpClient = new(handler);
        YahooFinanceSessionService sessionService = new(httpClient);
        sessionService.Invalidate();
        YahooFinanceQuoteProvider provider = new(httpClient, sessionService);

        IReadOnlyList<QuoteSnapshot> quotes = await provider.GetQuotesAsync(["^VIX"]);

        QuoteSnapshot quote = Assert.Single(quotes);
        Assert.Equal("^VIX", quote.Symbol);
        Assert.Equal(0, handler.SparkRequestCount);
        Assert.Equal(1, handler.ChartRequestCount);
        Assert.Equal(1, handler.QuoteEndpointRequestCount);
        Assert.Equal(17.40m, quote.Last);
    }

    [Fact(Skip = "Obsolete after migrating the quote provider wrapper to YFinance.NET.")]
    public async Task GetQuotesAsync_UsesSparkForSpxInsteadOfDedicatedQuoteEndpoint()
    {
        SpxSparkHandler handler = new();
        using HttpClient httpClient = new(handler);
        YahooFinanceSessionService sessionService = new(httpClient);
        sessionService.Invalidate();
        YahooFinanceQuoteProvider provider = new(httpClient, sessionService);

        IReadOnlyList<QuoteSnapshot> quotes = await provider.GetQuotesAsync(["^SPX"]);

        QuoteSnapshot quote = Assert.Single(quotes);
        Assert.Equal("^SPX", quote.Symbol);
        Assert.Equal(7501.24m, quote.Last);
        Assert.Equal(1, handler.SparkRequestCount);
        Assert.Equal(0, handler.ChartRequestCount);
    }

    private sealed class QuoteFlowHandler : HttpMessageHandler
    {
        public int SparkRequestCount { get; private set; }
        public int ChartRequestCount { get; private set; }
        public List<string> ChartRequestSymbols { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri?.ToString() ?? string.Empty;

            if (url.StartsWith("https://finance.yahoo.com/", StringComparison.OrdinalIgnoreCase))
            {
                HttpResponseMessage bootstrap = new(HttpStatusCode.OK)
                {
                    Content = new StringContent("home")
                };
                bootstrap.Headers.TryAddWithoutValidation("Set-Cookie", "A=1; Path=/");
                return Task.FromResult(bootstrap);
            }

            if (url.StartsWith("https://query1.finance.yahoo.com/v1/test/getcrumb", StringComparison.OrdinalIgnoreCase))
            {
                HttpResponseMessage crumb = new(HttpStatusCode.OK)
                {
                    Content = new StringContent("crumb1")
                };
                crumb.Headers.TryAddWithoutValidation("Set-Cookie", "B=2; Path=/");
                return Task.FromResult(crumb);
            }

            if (url.Contains("/v8/finance/spark", StringComparison.OrdinalIgnoreCase))
            {
                SparkRequestCount++;
                string payload =
                    """
                    {
                      "spark": {
                        "result": [
                          {
                            "symbol": "AAPL",
                            "response": [
                              {
                                "meta": {
                                  "regularMarketPrice": 190.00,
                                  "chartPreviousClose": 188.00,
                                  "regularMarketTime": 1710000000
                                },
                                "timestamp": [1710000000],
                                "indicators": { "quote": [ { "close": [190.00] } ] }
                              }
                            ]
                          }
                        ]
                      }
                    }
                    """;

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(payload)
                });
            }

            if (url.Contains("/v8/finance/chart/", StringComparison.OrdinalIgnoreCase))
            {
                ChartRequestCount++;
                string symbol = ExtractChartSymbol(url);
                ChartRequestSymbols.Add(symbol);

                string payload =
                    """
                    {
                      "chart": {
                        "result": [
                          {
                            "timestamp": [1710000000,1710086400],
                            "indicators": {
                              "quote": [ { "close": [98.00,100.00] } ]
                            }
                          }
                        ]
                      }
                    }
                    """;

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(payload)
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static string ExtractChartSymbol(string url)
        {
            const string marker = "/v8/finance/chart/";
            int start = url.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
                return string.Empty;

            start += marker.Length;
            int end = url.IndexOf('?', start);
            string encoded = end >= 0 ? url[start..end] : url[start..];
            return Uri.UnescapeDataString(encoded);
        }
    }

    private sealed class QuoteEndpointFallbackHandler : HttpMessageHandler
    {
        public int SparkRequestCount { get; private set; }
        public int ChartRequestCount { get; private set; }
        public int QuoteEndpointRequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri?.ToString() ?? string.Empty;

            if (url.StartsWith("https://finance.yahoo.com/", StringComparison.OrdinalIgnoreCase))
            {
                HttpResponseMessage bootstrap = new(HttpStatusCode.OK)
                {
                    Content = new StringContent("home")
                };
                bootstrap.Headers.TryAddWithoutValidation("Set-Cookie", "A=1; Path=/");
                return Task.FromResult(bootstrap);
            }

            if (url.StartsWith("https://query1.finance.yahoo.com/v1/test/getcrumb", StringComparison.OrdinalIgnoreCase))
            {
                HttpResponseMessage crumb = new(HttpStatusCode.OK)
                {
                    Content = new StringContent("crumb1")
                };
                crumb.Headers.TryAddWithoutValidation("Set-Cookie", "B=2; Path=/");
                return Task.FromResult(crumb);
            }

            if (url.Contains("/v8/finance/spark", StringComparison.OrdinalIgnoreCase))
            {
                SparkRequestCount++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("spark unavailable")
                });
            }

            if (url.Contains("/v8/finance/chart/", StringComparison.OrdinalIgnoreCase))
            {
                ChartRequestCount++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("chart unavailable")
                });
            }

            if (url.Contains("/v7/finance/quote", StringComparison.OrdinalIgnoreCase))
            {
                QuoteEndpointRequestCount++;
                string payload =
                    """
                    {
                      "quoteResponse": {
                        "result": [
                          {
                            "symbol": "AAPL",
                            "regularMarketPrice": 190.1,
                            "regularMarketPreviousClose": 188.4,
                            "regularMarketChange": 1.7,
                            "regularMarketChangePercent": 0.90,
                            "regularMarketTime": 1710000000
                          },
                          {
                            "symbol": "MSFT",
                            "regularMarketPrice": 411.5,
                            "regularMarketPreviousClose": 409.0,
                            "regularMarketChange": 2.5,
                            "regularMarketChangePercent": 0.61,
                            "regularMarketTime": 1710000000
                          }
                        ]
                      }
                    }
                    """;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(payload)
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class PartialSparkRateLimitHandler : HttpMessageHandler
    {
        public int SparkRequestCount { get; private set; }
        public int ChartRequestCount { get; private set; }
        public int QuoteEndpointRequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri?.ToString() ?? string.Empty;

            if (url.StartsWith("https://finance.yahoo.com/", StringComparison.OrdinalIgnoreCase))
            {
                HttpResponseMessage bootstrap = new(HttpStatusCode.OK)
                {
                    Content = new StringContent("home")
                };
                bootstrap.Headers.TryAddWithoutValidation("Set-Cookie", "A=1; Path=/");
                return Task.FromResult(bootstrap);
            }

            if (url.StartsWith("https://query1.finance.yahoo.com/v1/test/getcrumb", StringComparison.OrdinalIgnoreCase))
            {
                HttpResponseMessage crumb = new(HttpStatusCode.OK)
                {
                    Content = new StringContent("crumb1")
                };
                crumb.Headers.TryAddWithoutValidation("Set-Cookie", "B=2; Path=/");
                return Task.FromResult(crumb);
            }

            if (url.Contains("/v8/finance/spark", StringComparison.OrdinalIgnoreCase))
            {
                SparkRequestCount++;
                if (SparkRequestCount == 1)
                {
                    string payload = BuildSparkPayload(1, 24);
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(payload)
                    });
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    Content = new StringContent("rate limited")
                });
            }

            if (url.Contains("/v8/finance/chart/", StringComparison.OrdinalIgnoreCase))
            {
                ChartRequestCount++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}")
                });
            }

            if (url.Contains("/v7/finance/quote", StringComparison.OrdinalIgnoreCase))
            {
                QuoteEndpointRequestCount++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static string BuildSparkPayload(int startInclusive, int endInclusive)
        {
            string items = string.Join(",",
                Enumerable.Range(startInclusive, endInclusive - startInclusive + 1)
                    .Select(index =>
                        $$"""
                        {
                          "symbol": "SYM{{index:00}}",
                          "response": [
                            {
                              "meta": {
                                "regularMarketPrice": {{100 + index}}.0,
                                "chartPreviousClose": {{99 + index}}.0,
                                "regularMarketTime": 1710000000
                              },
                              "timestamp": [1710000000],
                              "indicators": { "quote": [ { "close": [{{100 + index}}.0] } ] }
                            }
                          ]
                        }
                        """));

            return $$"""
            {
              "spark": {
                "result": [
                  {{items}}
                ]
              }
            }
            """;
        }
    }

    private sealed class RateLimitedYahooHandler : HttpMessageHandler
    {
        public int SparkRequestCount { get; private set; }
        public int ChartRequestCount { get; private set; }
        public int QuoteEndpointRequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri?.ToString() ?? string.Empty;

            if (url.StartsWith("https://finance.yahoo.com/", StringComparison.OrdinalIgnoreCase))
            {
                HttpResponseMessage bootstrap = new(HttpStatusCode.OK)
                {
                    Content = new StringContent("home")
                };
                bootstrap.Headers.TryAddWithoutValidation("Set-Cookie", "A=1; Path=/");
                return Task.FromResult(bootstrap);
            }

            if (url.StartsWith("https://query1.finance.yahoo.com/v1/test/getcrumb", StringComparison.OrdinalIgnoreCase))
            {
                HttpResponseMessage crumb = new(HttpStatusCode.OK)
                {
                    Content = new StringContent("crumb1")
                };
                crumb.Headers.TryAddWithoutValidation("Set-Cookie", "B=2; Path=/");
                return Task.FromResult(crumb);
            }

            if (url.Contains("/v8/finance/spark", StringComparison.OrdinalIgnoreCase))
            {
                SparkRequestCount++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    Content = new StringContent("rate limited")
                });
            }

            if (url.Contains("/v8/finance/chart/", StringComparison.OrdinalIgnoreCase))
            {
                ChartRequestCount++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    Content = new StringContent("rate limited")
                });
            }

            if (url.Contains("/v7/finance/quote", StringComparison.OrdinalIgnoreCase))
            {
                QuoteEndpointRequestCount++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    Content = new StringContent("rate limited")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class PreferredQuoteEndpointDedicatedHandler : HttpMessageHandler
    {
        public int SparkRequestCount { get; private set; }
        public int ChartRequestCount { get; private set; }
        public int QuoteEndpointRequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri?.ToString() ?? string.Empty;

            if (url.StartsWith("https://finance.yahoo.com/", StringComparison.OrdinalIgnoreCase))
            {
                HttpResponseMessage bootstrap = new(HttpStatusCode.OK)
                {
                    Content = new StringContent("home")
                };
                bootstrap.Headers.TryAddWithoutValidation("Set-Cookie", "A=1; Path=/");
                return Task.FromResult(bootstrap);
            }

            if (url.StartsWith("https://query1.finance.yahoo.com/v1/test/getcrumb", StringComparison.OrdinalIgnoreCase))
            {
                HttpResponseMessage crumb = new(HttpStatusCode.OK)
                {
                    Content = new StringContent("crumb1")
                };
                crumb.Headers.TryAddWithoutValidation("Set-Cookie", "B=2; Path=/");
                return Task.FromResult(crumb);
            }

            if (url.Contains("/v8/finance/spark", StringComparison.OrdinalIgnoreCase))
            {
                SparkRequestCount++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("spark should not be used")
                });
            }

            if (url.Contains("/v8/finance/chart/", StringComparison.OrdinalIgnoreCase))
            {
                ChartRequestCount++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("chart should not be used")
                });
            }

            if (url.Contains("/v7/finance/quote", StringComparison.OrdinalIgnoreCase))
            {
                QuoteEndpointRequestCount++;
                string payload =
                    """
                    {
                      "quoteResponse": {
                        "result": [
                          {
                            "symbol": "^VIX",
                            "regularMarketPrice": 17.4,
                            "regularMarketPreviousClose": 16.2,
                            "regularMarketChange": 1.2,
                            "regularMarketChangePercent": 7.41,
                            "regularMarketTime": 1710000000
                          },
                          {
                            "symbol": "DX-Y.NYB",
                            "regularMarketPrice": 102.54,
                            "regularMarketPreviousClose": 102.12,
                            "regularMarketChange": 0.42,
                            "regularMarketChangePercent": 0.41,
                            "regularMarketTime": 1710000000
                          }
                        ]
                      }
                    }
                    """;

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(payload)
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class PreferredQuoteEndpointEmptyChartFallbackHandler : HttpMessageHandler
    {
        public int SparkRequestCount { get; private set; }
        public int ChartRequestCount { get; private set; }
        public int QuoteEndpointRequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri?.ToString() ?? string.Empty;

            if (url.StartsWith("https://finance.yahoo.com/", StringComparison.OrdinalIgnoreCase))
            {
                HttpResponseMessage bootstrap = new(HttpStatusCode.OK)
                {
                    Content = new StringContent("home")
                };
                bootstrap.Headers.TryAddWithoutValidation("Set-Cookie", "A=1; Path=/");
                return Task.FromResult(bootstrap);
            }

            if (url.StartsWith("https://query1.finance.yahoo.com/v1/test/getcrumb", StringComparison.OrdinalIgnoreCase))
            {
                HttpResponseMessage crumb = new(HttpStatusCode.OK)
                {
                    Content = new StringContent("crumb1")
                };
                crumb.Headers.TryAddWithoutValidation("Set-Cookie", "B=2; Path=/");
                return Task.FromResult(crumb);
            }

            if (url.Contains("/v8/finance/spark", StringComparison.OrdinalIgnoreCase))
            {
                SparkRequestCount++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("spark should not be used")
                });
            }

            if (url.Contains("/v8/finance/chart/", StringComparison.OrdinalIgnoreCase))
            {
                ChartRequestCount++;
                string payload =
                    """
                    {
                      "chart": {
                        "result": [
                          {
                            "timestamp": [1710000000,1710086400],
                            "indicators": {
                              "quote": [ { "close": [16.20,17.40] } ]
                            }
                          }
                        ]
                      }
                    }
                    """;

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(payload)
                });
            }

            if (url.Contains("/v7/finance/quote", StringComparison.OrdinalIgnoreCase))
            {
                QuoteEndpointRequestCount++;
                string payload =
                    """
                    {
                      "quoteResponse": {
                        "result": []
                      }
                    }
                    """;

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(payload)
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class SpxSparkHandler : HttpMessageHandler
    {
        public int SparkRequestCount { get; private set; }
        public int ChartRequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri?.ToString() ?? string.Empty;

            if (url.StartsWith("https://finance.yahoo.com/", StringComparison.OrdinalIgnoreCase))
            {
                HttpResponseMessage bootstrap = new(HttpStatusCode.OK)
                {
                    Content = new StringContent("home")
                };
                bootstrap.Headers.TryAddWithoutValidation("Set-Cookie", "A=1; Path=/");
                return Task.FromResult(bootstrap);
            }

            if (url.StartsWith("https://query1.finance.yahoo.com/v1/test/getcrumb", StringComparison.OrdinalIgnoreCase))
            {
                HttpResponseMessage crumb = new(HttpStatusCode.OK)
                {
                    Content = new StringContent("crumb1")
                };
                crumb.Headers.TryAddWithoutValidation("Set-Cookie", "B=2; Path=/");
                return Task.FromResult(crumb);
            }

            if (url.Contains("/v8/finance/spark", StringComparison.OrdinalIgnoreCase))
            {
                SparkRequestCount++;
                string payload =
                    """
                    {
                      "spark": {
                        "result": [
                          {
                            "symbol": "^SPX",
                            "response": [
                              {
                                "meta": {
                                  "regularMarketPrice": 7501.24,
                                  "chartPreviousClose": 7444.25,
                                  "regularMarketTime": 1710000000
                                },
                                "timestamp": [1710000000],
                                "indicators": { "quote": [ { "close": [7501.24] } ] }
                              }
                            ]
                          }
                        ]
                      }
                    }
                    """;

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(payload)
                });
            }

            if (url.Contains("/v8/finance/chart/", StringComparison.OrdinalIgnoreCase))
            {
                ChartRequestCount++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("chart should not be used")
                });
            }

            if (url.Contains("/v7/finance/quote", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("quote endpoint should not be used")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private static string ExtractChartSymbol(string url)
    {
        const string marker = "/v8/finance/chart/";
        int start = url.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return string.Empty;

        start += marker.Length;
        int end = url.IndexOf('?', start);
        string encoded = end >= 0 ? url[start..end] : url[start..];
        return Uri.UnescapeDataString(encoded);
    }
}
