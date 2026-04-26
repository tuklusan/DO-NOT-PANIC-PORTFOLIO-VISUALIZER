using System.Net;
using System.Net.Http;
using PortfolioSaver.Data.Providers;
using Xunit;

namespace PortfolioSaver.Tests.Providers;

public sealed class StooqGlobalMarketQuoteProviderTests
{
    [Fact]
    public async Task GetQuotesAsync_MapsCanonicalGlobalSymbolsToStooqSymbols()
    {
        StooqHandler handler = new();
        using HttpClient httpClient = new(handler);
        StooqGlobalMarketQuoteProvider provider = new(httpClient);

        var quotes = await provider.GetQuotesAsync(["^FTSE", "DX-Y.NYB", "^SPX", "INDY.US", "EWA.US", "^NYA"]);

        Assert.Equal(5, quotes.Count);
        Assert.Contains("^ukx", handler.RequestedSymbols, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("dx.f", handler.RequestedSymbols, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("^spx", handler.RequestedSymbols, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("indy.us", handler.RequestedSymbols, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("ewa.us", handler.RequestedSymbols, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("^NYA", handler.RequestedSymbols, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(quotes, quote => quote.Symbol == "^FTSE" && quote.Last == 8123.45m);
        Assert.Contains(quotes, quote => quote.Symbol == "DX-Y.NYB" && quote.Last == 104.10m);
        Assert.Contains(quotes, quote => quote.Symbol == "^SPX" && quote.Last == 7109.14m);
        Assert.Contains(quotes, quote => quote.Symbol == "INDY.US" && quote.Last == 44.82m);
        Assert.Contains(quotes, quote => quote.Symbol == "EWA.US" && quote.Last == 30.05m);
    }

    [Fact]
    public void CanResolve_ReturnsFalseForUnverifiedSymbols()
    {
        Assert.True(StooqGlobalMarketQuoteProvider.CanResolve("^FTSE"));
        Assert.True(StooqGlobalMarketQuoteProvider.CanResolve("DX-Y.NYB"));
        Assert.True(StooqGlobalMarketQuoteProvider.CanResolve("^SPX"));
        Assert.True(StooqGlobalMarketQuoteProvider.CanResolve("INDY.US"));
        Assert.True(StooqGlobalMarketQuoteProvider.CanResolve("EWA.US"));
        Assert.False(StooqGlobalMarketQuoteProvider.CanResolve("^NYA"));
        Assert.False(StooqGlobalMarketQuoteProvider.CanResolve("^AXJO"));
        Assert.False(StooqGlobalMarketQuoteProvider.CanResolve("^NSEI"));
    }

    private sealed class StooqHandler : HttpMessageHandler
    {
        public List<string> RequestedSymbols { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string symbol = GetQueryValue(request.RequestUri, "s");
            RequestedSymbols.Add(symbol);
            string payload = symbol.ToUpperInvariant() switch
            {
                "^UKX" => "Symbol,Date,Time,Open,High,Low,Close,Volume,Name\n^UKX,2026-04-21,09:58:57,8100.00,8130.00,8090.00,8123.45,,UKX100\n",
                "DX.F" => "Symbol,Date,Time,Open,High,Low,Close,Volume,Name\nDX.F,2026-04-21,09:59:33,103.85,104.20,103.80,104.10,,US DOLLAR INDEX\n",
                "^SPX" => "Symbol,Date,Time,Open,High,Low,Close,Volume,Name\n^SPX,2026-04-20,23:00:00,7117.05,7122.65,7084.41,7109.14,2628827402,SPX500\n",
                "INDY.US" => "Symbol,Date,Time,Open,High,Low,Close,Volume,Name\nINDY.US,2026-04-20,22:00:18,44.83,44.898,44.72,44.82,132819,ISHARES INDIA 50 ETF\n",
                "EWA.US" => "Symbol,Date,Time,Open,High,Low,Close,Volume,Name\nEWA.US,2026-04-20,22:00:18,30.09,30.10,29.88,30.05,1866724,ISHARES MSCI AUSTRALIA ETF\n",
                _ => $"Symbol,Date,Time,Open,High,Low,Close,Volume,Name\n{symbol.ToUpperInvariant()},N/D,N/D,N/D,N/D,N/D,N/D,N/D,{symbol.ToUpperInvariant()}\n"
            };

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload)
            });
        }
    }

    private static string GetQueryValue(Uri? uri, string key)
    {
        if (uri is null)
            return string.Empty;

        foreach (string segment in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = segment.Split('=', 2);
            if (parts.Length != 2)
                continue;

            if (string.Equals(Uri.UnescapeDataString(parts[0]), key, StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(parts[1]);
        }

        return string.Empty;
    }
}
