using System.Net;
using System.Net.Http;
using System.Text;
using PortfolioSaver.Data.Providers;
using Xunit;

namespace PortfolioSaver.Tests.Providers;

public sealed class TiingoQuoteProviderTests
{
    [Fact]
    public async Task GetQuotesAsync_FallsBackToIexWhenDailyPricesReturnsBadRequest()
    {
        TiingoHandler handler = new();
        using HttpClient client = new(handler);
        TiingoQuoteProvider provider = new(client, "tiingo-key");

        var quotes = await provider.GetQuotesAsync(["AAPL"]);

        Assert.Single(quotes);
        Assert.Equal("AAPL", quotes[0].Symbol);
        Assert.True(quotes[0].Last > 0);
        Assert.Equal(1, handler.DailyRequestCount);
        Assert.Equal(1, handler.IexRequestCount);
    }

    [Fact]
    public async Task GetQuotesAsync_DailyQuoteRequest_DoesNotUseLegacyResampleFrequency()
    {
        TiingoHandler handler = new();
        using HttpClient client = new(handler);
        TiingoQuoteProvider provider = new(client, "tiingo-key");

        _ = await provider.GetQuotesAsync(["AAPL"]);

        Assert.DoesNotContain(handler.Requests, uri => uri.Contains("resampleFreq=1day", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class TiingoHandler : HttpMessageHandler
    {
        public int DailyRequestCount { get; private set; }
        public int IexRequestCount { get; private set; }
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string uri = request.RequestUri!.ToString();
            Requests.Add(uri);

            if (uri.Contains("/tiingo/daily/AAPL/prices", StringComparison.OrdinalIgnoreCase))
            {
                DailyRequestCount++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
            }

            if (uri.Contains("/iex/", StringComparison.OrdinalIgnoreCase))
            {
                IexRequestCount++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """[{"ticker":"AAPL","last":201.12,"prevClose":199.01,"quoteTimestamp":"2026-05-02T10:00:00Z"}]""",
                        Encoding.UTF8,
                        "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
