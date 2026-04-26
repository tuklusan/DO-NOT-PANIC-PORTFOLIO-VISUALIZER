using System.Net;
using System.Net.Http;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Data.Interfaces;
using PortfolioSaver.Data.Providers;
using Xunit;

namespace PortfolioSaver.Tests.Providers;

[Collection("YahooSessionSerial")]
public sealed class HybridHistoricalDataProviderTests
{
    [Fact]
    public async Task GetHistoryAsync_UsesYahooSparkBatch_ThenChartFallbackForUnresolvedSymbols()
    {
        HistoricalFlowHandler handler = new();
        using HttpClient httpClient = new(handler);
        FakeHistoricalCache cache = new();
        HybridHistoricalDataProvider provider = new(
            cache,
            httpClient: httpClient,
            finnhubApiKey: string.Empty,
            twelveDataApiKey: string.Empty,
            cacheFreshness: TimeSpan.FromHours(12));

        IReadOnlyList<TickerHistorySnapshot> snapshots = await provider.GetHistoryAsync(["AAPL", "MSFT"], lookbackDays: 14);

        Assert.Equal(2, snapshots.Count);
        Assert.All(snapshots, snapshot => Assert.True(snapshot.Points.Count > 0));

        Assert.Equal(1, handler.SparkRequestCount);
        Assert.Equal(1, handler.ChartRequestCount);
        Assert.Contains("MSFT", handler.ChartRequestSymbols, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("AAPL", handler.ChartRequestSymbols, StringComparer.OrdinalIgnoreCase);
    }

    private sealed class FakeHistoricalCache : IHistoricalCacheService
    {
        private readonly Dictionary<string, TickerHistorySnapshot> _store = new(StringComparer.OrdinalIgnoreCase);

        public Task<TickerHistorySnapshot?> LoadAsync(string symbol, CancellationToken cancellationToken = default)
            => Task.FromResult(_store.TryGetValue(symbol, out TickerHistorySnapshot? snapshot) ? snapshot : null);

        public Task SaveAsync(TickerHistorySnapshot snapshot, CancellationToken cancellationToken = default)
        {
            _store[snapshot.Symbol] = snapshot;
            return Task.CompletedTask;
        }

        public Task PurgeExpiredAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class HistoricalFlowHandler : HttpMessageHandler
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
                long ts1 = DateTimeOffset.UtcNow.AddDays(-2).ToUnixTimeSeconds();
                long ts2 = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds();
                string payload =
                    $$"""
                    {
                      "spark": {
                        "result": [
                          {
                            "symbol": "AAPL",
                            "response": [
                              {
                                "timestamp": [{{ts1}},{{ts2}}],
                                "indicators": { "quote": [ { "close": [190.00,191.25] } ] }
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

                long ts1 = DateTimeOffset.UtcNow.AddDays(-3).ToUnixTimeSeconds();
                long ts2 = DateTimeOffset.UtcNow.AddDays(-2).ToUnixTimeSeconds();
                long ts3 = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds();
                string payload =
                    $$"""
                    {
                      "chart": {
                        "result": [
                          {
                            "timestamp": [{{ts1}},{{ts2}},{{ts3}}],
                            "indicators": {
                              "quote": [ { "close": [330.50,331.00,332.25] } ]
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
}
