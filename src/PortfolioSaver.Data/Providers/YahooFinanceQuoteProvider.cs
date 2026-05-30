using PortfolioSaver.Core.Models;
using PortfolioSaver.Data.Interfaces;
using PortfolioSaver.Data.Services;
using PortfolioSaver.Shared.Diagnostics;
using YFinance.NET.Protocol.Dtos;

namespace PortfolioSaver.Data.Providers;

public sealed class YahooFinanceQuoteProvider : IQuoteProvider
{
    public YahooFinanceQuoteProvider(HttpClient httpClient)
    {
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

        string operationId = YFinanceRuntimeClientFactory.CreateOperationId("quotes");
        Dictionary<string, string> requestByOriginal = requestedSymbols.ToDictionary(
            symbol => symbol,
            YFinanceSymbolMapper.ToRequestSymbol,
            StringComparer.OrdinalIgnoreCase);
        TraceLog.InfoState(
            "YFinanceUiBridge",
            "QuoteRequestStart",
            [new("operation_id", operationId), new("requested_count", requestedSymbols.Count), new("symbols", requestedSymbols), new("request_symbols", requestByOriginal.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToList())]);

        QuotesResponseDto resolved;
        try
        {
            resolved = await YFinanceRuntimeClientFactory
                .RunSerializedAsync(
                    "quotes",
                    operationId,
                    (client, token) => client.GetQuotesAsync(requestByOriginal.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), token),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            TraceLog.WarnState(
                "YFinanceUiBridge",
                "QuoteRequestFailed",
                [new("operation_id", operationId), new("requested_count", requestedSymbols.Count), new("symbols", requestedSymbols), new("message", ex.Message)]);
            throw;
        }

        Dictionary<string, QuoteSnapshot> results = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, QuoteDto> byResponseKey = new(StringComparer.OrdinalIgnoreCase);
        foreach (QuoteDto quote in resolved.Quotes)
        {
            if (!string.IsNullOrWhiteSpace(quote.Symbol))
                byResponseKey[quote.Symbol] = quote;

            string responseKey = YFinanceSymbolMapper.ToResponseMatchKey(quote.Symbol);
            if (!string.IsNullOrWhiteSpace(responseKey))
                byResponseKey[responseKey] = quote;
        }

        foreach ((string originalSymbol, string requestSymbol) in requestByOriginal)
        {
            if (!byResponseKey.TryGetValue(requestSymbol, out QuoteDto? quote) &&
                !byResponseKey.TryGetValue(YFinanceSymbolMapper.ToResponseMatchKey(requestSymbol), out quote))
                continue;

            QuoteSnapshot mapped = MapQuote(originalSymbol, quote);
            if (mapped.Last is null && mapped.PreviousClose is null)
                continue;

            results[originalSymbol] = mapped;
        }

        TraceLog.InfoState(
            "YFinanceNetQuoteProvider",
            "QuoteBatchMapped",
            [new("operation_id", operationId), new("requested_count", requestedSymbols.Count), new("resolved_count", results.Count), new("symbols", requestedSymbols)]);
        TraceLog.InfoState(
            "YFinanceUiBridge",
            "QuoteRequestComplete",
            [new("operation_id", operationId), new("requested_count", requestedSymbols.Count), new("resolved_count", results.Count), new("resolved_symbols", results.Keys.ToList())]);

        if (results.Count == 0)
            throw new InvalidOperationException("YFinance.NET server returned no matching quotes.");

        List<string> unresolved = requestedSymbols
            .Where(symbol => !results.ContainsKey(symbol))
            .ToList();
        if (unresolved.Count > 0 && results.Count > 0)
            throw new PartialQuoteResultException(
                $"YFinance.NET server returned partial quotes. Missing: {string.Join(", ", unresolved)}",
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

    private static QuoteSnapshot MapQuote(string originalSymbol, QuoteDto quote)
    {
        decimal? last = YFinanceSymbolMapper.NormalizeNumericValue(originalSymbol, quote.RegularMarketPrice);
        decimal? previousClose = YFinanceSymbolMapper.NormalizeNumericValue(originalSymbol, quote.RegularMarketPreviousClose);
        decimal? change = YFinanceSymbolMapper.NormalizeNumericValue(originalSymbol, quote.RegularMarketChange);
        decimal? changePercent = quote.RegularMarketChangePercent;
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
            IsStale = quote.Cache.Stale
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
