using System.Net;
using System.Net.Http;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Data.Providers;
using Xunit;

namespace PortfolioSaver.Tests.Providers;

public sealed class TwelveDataQuoteProviderTests
{
    [Fact]
    public async Task GetQuotesAsync_LeavesUnsupportedYahooStyleIndexSymbolsUnmapped()
    {
        AliasAwareHandler handler = new();
        using HttpClient httpClient = new(handler);
        TwelveDataQuoteProvider provider = new(httpClient, "test-key");

        IReadOnlyList<QuoteSnapshot> quotes = await provider.GetQuotesAsync(["^FTSE", "DX-Y.NYB"]);

        Assert.Empty(quotes);
        Assert.Contains("^FTSE", handler.RequestedSymbols, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("DX-Y.NYB", handler.RequestedSymbols, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("FTSE", handler.RequestedSymbols, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("DXY", handler.RequestedSymbols, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetQuotesAsync_SkipsInvalidAliasedSymbolWithoutPoisoningOtherResults()
    {
        InvalidThenValidHandler handler = new();
        using HttpClient httpClient = new(handler);
        TwelveDataQuoteProvider provider = new(httpClient, "test-key");

        IReadOnlyList<QuoteSnapshot> quotes = await provider.GetQuotesAsync(["^NYA", "AAPL"]);

        QuoteSnapshot quote = Assert.Single(quotes);
        Assert.Equal("AAPL", quote.Symbol);
        Assert.Contains("^NYA", handler.RequestedSymbols, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("AAPL", handler.RequestedSymbols, StringComparer.OrdinalIgnoreCase);
    }

    private sealed class AliasAwareHandler : HttpMessageHandler
    {
        public List<string> RequestedSymbols { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string symbol = GetQueryValue(request.RequestUri, "symbol");
            RequestedSymbols.Add(symbol);

            string payload = symbol.ToUpperInvariant() switch
            {
                _ => """{"code":400,"message":"symbol is not found"}"""
            };

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload)
            });
        }
    }

    private sealed class InvalidThenValidHandler : HttpMessageHandler
    {
        public List<string> RequestedSymbols { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string symbol = GetQueryValue(request.RequestUri, "symbol");
            RequestedSymbols.Add(symbol);

            string payload = string.Equals(symbol, "AAPL", StringComparison.OrdinalIgnoreCase)
                ? """{"symbol":"AAPL","close":"210.00","previous_close":"208.00","percent_change":"0.96"}"""
                : """{"code":400,"message":"symbol is not found"}""";

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
