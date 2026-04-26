using System.Net;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Data.Providers;
using Xunit;

namespace PortfolioSaver.Tests.Providers;

public sealed class CboeVolatilityIndexQuoteProviderTests
{
    [Fact]
    public async Task GetQuotesAsync_ReturnsLatestAndPreviousVolatilityIndexCloses()
    {
        CboeHistoryHandler handler = new();
        using HttpClient httpClient = new(handler);
        CboeVolatilityIndexQuoteProvider provider = new(httpClient);

        IReadOnlyList<QuoteSnapshot> quotes = await provider.GetQuotesAsync(["^VIX", "^VIX3M"]);

        Assert.Equal(2, quotes.Count);
        Assert.Contains("VIX_History.csv", handler.RequestedFiles, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("VIX3M_History.csv", handler.RequestedFiles, StringComparer.OrdinalIgnoreCase);

        QuoteSnapshot vix = Assert.Single(quotes.Where(quote => quote.Symbol == "^VIX"));
        Assert.Equal(17.82m, vix.Last);
        Assert.Equal(18.10m, vix.PreviousClose);
        Assert.Equal(-0.28m, vix.Change);

        QuoteSnapshot vix3m = Assert.Single(quotes.Where(quote => quote.Symbol == "^VIX3M"));
        Assert.Equal(19.44m, vix3m.Last);
        Assert.Equal(19.31m, vix3m.PreviousClose);
        Assert.Equal(0.13m, vix3m.Change);
    }

    private sealed class CboeHistoryHandler : HttpMessageHandler
    {
        public List<string> RequestedFiles { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string file = Path.GetFileName(request.RequestUri?.AbsolutePath ?? string.Empty);
            RequestedFiles.Add(file);

            string payload = string.Equals(file, "VIX_History.csv", StringComparison.OrdinalIgnoreCase)
                ? """
                  DATE,OPEN,HIGH,LOW,CLOSE
                  04/17/2026,18.10,18.20,17.70,18.10
                  04/18/2026,17.80,17.90,17.50,17.82
                  """
                : """
                  DATE,OPEN,HIGH,LOW,CLOSE
                  04/17/2026,19.30,19.50,19.20,19.31
                  04/18/2026,19.40,19.60,19.30,19.44
                  """;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload)
            });
        }
    }
}
