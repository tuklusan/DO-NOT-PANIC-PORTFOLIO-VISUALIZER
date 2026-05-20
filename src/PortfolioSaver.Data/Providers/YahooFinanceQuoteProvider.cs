using PortfolioSaver.Core.Models;
using PortfolioSaver.Data.Interfaces;
using PortfolioSaver.Data.Services;
using PortfolioSaver.Shared.Diagnostics;
using YFinance.NET.Api;
using YFinanceQuoteSnapshot = YFinance.NET.Models.QuoteSnapshot;

namespace PortfolioSaver.Data.Providers;

public sealed class YahooFinanceQuoteProvider : IQuoteProvider
{
    private readonly YFinanceClient _client;

    public YahooFinanceQuoteProvider(HttpClient httpClient, YahooFinanceSessionService? sessionService = null)
        : this(YFinanceRuntimeClientFactory.GetSharedClient())
    {
    }

    internal YahooFinanceQuoteProvider(YFinanceClient client)
    {
        _client = client;
    }

    public async Task<IReadOnlyList<QuoteSnapshot>> GetQuotesAsync(IEnumerable<string> symbols, CancellationToken cancellationToken = default)
    {
        List<string> requestedSymbols = symbols
            .Select(YFinanceSymbolMapper.Normalize)
            .Where(static symbol => !string.IsNullOrWhiteSpace(symbol))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (requestedSymbols.Count == 0)
            return [];

        Dictionary<string, string> requestByOriginal = requestedSymbols.ToDictionary(
            symbol => symbol,
            YFinanceSymbolMapper.ToRequestSymbol,
            StringComparer.OrdinalIgnoreCase);
        IReadOnlyDictionary<string, YFinanceQuoteSnapshot> resolved = await YFinanceRuntimeClientFactory
            .RunSerializedAsync(
                "quotes",
                (client, token) => client
                    .Tickers(requestByOriginal.Values.Distinct(StringComparer.OrdinalIgnoreCase))
                    .GetQuotesAsync(token),
                cancellationToken)
            .ConfigureAwait(false);

        Dictionary<string, QuoteSnapshot> results = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string originalSymbol, string requestSymbol) in requestByOriginal)
        {
            if (!resolved.TryGetValue(requestSymbol, out YFinanceQuoteSnapshot? quote))
                continue;

            QuoteSnapshot mapped = MapQuote(originalSymbol, quote);
            if (mapped.Last is null && mapped.PreviousClose is null)
                continue;

            results[originalSymbol] = mapped;
        }

        TraceLog.InfoState(
            "YFinanceNetQuoteProvider",
            "QuoteBatchMapped",
            [new("requested_count", requestedSymbols.Count), new("resolved_count", results.Count), new("symbols", requestedSymbols)]);

        if (results.Count == 0)
            throw new InvalidOperationException("YFinance.NET returned no matching quotes.");

        List<string> unresolved = requestedSymbols
            .Where(symbol => !results.ContainsKey(symbol))
            .ToList();
        if (unresolved.Count > 0 && results.Count > 0)
            throw new PartialQuoteResultException(
                $"YFinance.NET returned partial quotes. Missing: {string.Join(", ", unresolved)}",
                requestedSymbols.Where(results.ContainsKey).Select(symbol => results[symbol]).ToList());

        return requestedSymbols.Where(results.ContainsKey).Select(symbol => results[symbol]).ToList();
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            IReadOnlyList<QuoteSnapshot> quotes = await GetQuotesAsync(["AAPL"], cancellationToken).ConfigureAwait(false);
            return quotes.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static QuoteSnapshot MapQuote(string originalSymbol, YFinanceQuoteSnapshot quote)
    {
        decimal? last = YFinanceSymbolMapper.NormalizeNumericValue(originalSymbol, quote.RegularMarketPrice);
        decimal? previousClose = YFinanceSymbolMapper.NormalizeNumericValue(originalSymbol, quote.RegularMarketPreviousClose);
        decimal? change = YFinanceSymbolMapper.NormalizeNumericValue(originalSymbol, quote.RegularMarketChange);
        decimal? changePercent = quote.ComputedChangePercent;
        if (changePercent is null && last is decimal current && previousClose is decimal prior && prior != 0m)
            changePercent = ((current - prior) / prior) * 100m;

        return new QuoteSnapshot
        {
            Symbol = originalSymbol,
            Last = last,
            Change = change,
            ChangePercent = changePercent,
            PreviousClose = previousClose,
            Currency = quote.Currency ?? "USD",
            MarketSession = YFinanceSymbolMapper.MapMarketSession(quote.MarketState),
            ProviderTimestampUtc = null,
            FetchTimestampUtc = DateTimeOffset.UtcNow,
            IsStale = false
        };
    }
}

public sealed class PartialQuoteResultException : HttpRequestException
{
    public PartialQuoteResultException(string message, IReadOnlyList<QuoteSnapshot> partialQuotes)
        : base(message)
    {
        PartialQuotes = partialQuotes;
    }

    public IReadOnlyList<QuoteSnapshot> PartialQuotes { get; }
}
