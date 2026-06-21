using PortfolioSaver.Core.Models;
using PortfolioSaver.Data.Providers;
using YFinance.NET.Protocol.Dtos;
using Xunit;

namespace PortfolioSaver.Tests.Providers;

public sealed class YahooFinanceQuoteProviderTests
{
    [Fact]
    public async Task GetQuotesAsync_ReturnsResolvedSymbolsWhenResponseIsPartial()
    {
        YahooFinanceQuoteProvider provider = new((_, _, _) => Task.FromResult(CreatePartialResponse()));

        IReadOnlyList<QuoteSnapshot> quotes = await provider.GetQuotesAsync(["AAPL", "NOT-A-SYMBOL"]);

        QuoteSnapshot quote = Assert.Single(quotes);
        Assert.Equal("AAPL", quote.Symbol);
        Assert.Equal(192.34m, quote.Last);
    }

    [Fact]
    public async Task GetQuotesAsync_CanThrowPartialResultExceptionForCompatibility()
    {
        YahooFinanceQuoteProvider provider = new((_, _, _) => Task.FromResult(CreatePartialResponse()), throwOnPartial: true);

        PartialQuoteResultException ex = await Assert.ThrowsAsync<PartialQuoteResultException>(() => provider.GetQuotesAsync(["AAPL", "NOT-A-SYMBOL"]));

        QuoteSnapshot quote = Assert.Single(ex.PartialQuotes);
        Assert.Equal("AAPL", quote.Symbol);
    }

    [Fact]
    public async Task GetQuotesAsync_ThrowsWhenNoRequestedSymbolsResolve()
    {
        YahooFinanceQuoteProvider provider = new((_, _, _) => Task.FromResult(new QuotesResponseDto([], ["NOT-A-SYMBOL"])));

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetQuotesAsync(["NOT-A-SYMBOL"]));

        Assert.Contains("no matching quotes", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetQuotesAsync_PropagatesIntentionalCancellation()
    {
        using CancellationTokenSource cts = new();
        YahooFinanceQuoteProvider provider = new((_, _, token) => Task.FromCanceled<QuotesResponseDto>(token));

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetQuotesAsync(["AAPL"], cts.Token));
    }

    [Fact]
    public void MapQuotesResponse_PreservesResolvedSymbolsWhenResponseIsPartial()
    {
        QuotesResponseDto response = CreatePartialResponse();

        IReadOnlyList<QuoteSnapshot> mapped = YahooFinanceQuoteProvider.MapQuotesResponse(
            ["AAPL", "NOT-A-SYMBOL"],
            response,
            "test-partial");

        QuoteSnapshot quote = Assert.Single(mapped);
        Assert.Equal("AAPL", quote.Symbol);
        Assert.Equal(192.34m, quote.Last);
    }

    [Fact]
    public void MapQuotesResponse_ReturnsEmptyWhenNoRequestedSymbolsResolve()
    {
        QuotesResponseDto response = new([], ["NOT-A-SYMBOL"]);

        IReadOnlyList<QuoteSnapshot> mapped = YahooFinanceQuoteProvider.MapQuotesResponse(
            ["NOT-A-SYMBOL"],
            response,
            "test-empty");

        Assert.Empty(mapped);
    }

    [Fact]
    public async Task GetQuotesAsync_HandlesEmptyAndDuplicateInput()
    {
        int lookupCount = 0;
        YahooFinanceQuoteProvider provider = new((_, _, _) =>
        {
            lookupCount++;
            return Task.FromResult(CreatePartialResponse());
        });

        IReadOnlyList<QuoteSnapshot> empty = await provider.GetQuotesAsync([]);
        IReadOnlyList<QuoteSnapshot> duplicate = await provider.GetQuotesAsync(["AAPL", "aapl"]);

        Assert.Empty(empty);
        QuoteSnapshot quote = Assert.Single(duplicate);
        Assert.Equal("AAPL", quote.Symbol);
        Assert.Equal(1, lookupCount);
    }

    private static QuotesResponseDto CreatePartialResponse()
        => new(
            [
                new QuoteDto(
                    "AAPL",
                    "Apple",
                    "Apple Inc.",
                    "Apple",
                    "USD",
                    "NMS",
                    "America/New_York",
                    "EDT",
                    "EQUITY",
                    "REGULAR",
                    192.34m,
                    190.00m,
                    191.00m,
                    193.00m,
                    189.50m,
                    2.34m,
                    1.23m,
                    3000000000000,
                    123456789,
                    DateTimeOffset.UtcNow,
                    new CacheMetadataDto("live", 0, false))
            ],
            ["NOT-A-SYMBOL"]);
}
