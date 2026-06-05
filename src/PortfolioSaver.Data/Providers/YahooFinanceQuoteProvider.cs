using PortfolioSaver.Core.Models;
using PortfolioSaver.Data.Interfaces;
using PortfolioSaver.Data.Services;
using PortfolioSaver.Shared.Diagnostics;
using YFinance.NET.Protocol.Dtos;

namespace PortfolioSaver.Data.Providers;

public sealed class YahooFinanceQuoteProvider : IQuoteProvider
{
    private readonly Func<IReadOnlyList<string>, string, CancellationToken, Task<QuotesResponseDto>> _lookupQuotesAsync;
    private readonly bool _throwOnPartial;

    public YahooFinanceQuoteProvider(HttpClient httpClient)
        : this(httpClient, throwOnPartial: true)
    {
    }

    public YahooFinanceQuoteProvider(HttpClient httpClient, bool throwOnPartial)
    {
        // The app now talks to YFinance.NET through the owned runtime client; HttpClient remains in the signature for legacy call-site compatibility.
        _lookupQuotesAsync = LookupQuotesAsync;
        _throwOnPartial = throwOnPartial;
    }

    internal YahooFinanceQuoteProvider(
        Func<IReadOnlyList<string>, string, CancellationToken, Task<QuotesResponseDto>> lookupQuotesAsync,
        bool throwOnPartial = false)
    {
        _lookupQuotesAsync = lookupQuotesAsync;
        _throwOnPartial = throwOnPartial;
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
        Dictionary<string, string> requestByOriginal = BuildRequestMap(requestedSymbols);
        TraceLog.InfoState(
            "YFinanceUiBridge",
            "QuoteRequestStart",
            [new("operation_id", operationId), new("requested_count", requestedSymbols.Count), new("symbols", requestedSymbols), new("request_symbols", requestByOriginal.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToList())]);

        QuotesResponseDto resolved;
        try
        {
            resolved = await _lookupQuotesAsync(
                    requestByOriginal.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    operationId,
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

        TraceLog.InfoState(
            "YFinanceNetQuoteProvider",
            "QuoteResponsePayload",
            [
                new("operation_id", operationId),
                new("quote_count", resolved.Quotes.Count),
                new("missing_count", resolved.MissingSymbols.Count),
                new("response_symbols", resolved.Quotes.Select(static quote => quote.Symbol).ToList())
            ]);

        IReadOnlyList<QuoteSnapshot> results = MapQuotesResponse(requestedSymbols, requestByOriginal, resolved, operationId);

        if (results.Count == 0)
            throw new InvalidOperationException("YFinance.NET server returned no matching quotes.");

        List<string> unresolved = requestedSymbols
            .Where(symbol => results.All(quote => !string.Equals(quote.Symbol, symbol, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (_throwOnPartial && unresolved.Count > 0)
            throw new PartialQuoteResultException(
                $"YFinance.NET server returned partial quotes. Missing: {string.Join(", ", unresolved)}",
                results);

        return results;
    }

    private static async Task<QuotesResponseDto> LookupQuotesAsync(
        IReadOnlyList<string> requestSymbols,
        string operationId,
        CancellationToken cancellationToken)
        => await YFinanceRuntimeClientFactory
            .RunSerializedAsync(
                "quotes",
                operationId,
                (client, token) => client.GetQuotesAsync(requestSymbols, token),
                cancellationToken)
            .ConfigureAwait(false);

    internal static IReadOnlyList<QuoteSnapshot> MapQuotesResponse(
        IReadOnlyList<string> requestedSymbols,
        QuotesResponseDto resolved,
        string operationId)
        => MapQuotesResponse(requestedSymbols, BuildRequestMap(requestedSymbols), resolved, operationId);

    private static IReadOnlyList<QuoteSnapshot> MapQuotesResponse(
        IReadOnlyList<string> requestedSymbols,
        IReadOnlyDictionary<string, string> requestByOriginal,
        QuotesResponseDto resolved,
        string operationId)
    {
        ArgumentNullException.ThrowIfNull(resolved);

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
            {
                TraceLog.WarnState(
                    "YFinanceNetQuoteProvider",
                    "QuoteResponseNoMatch",
                    [
                        new("operation_id", operationId),
                        new("original_symbol", originalSymbol),
                        new("request_symbol", requestSymbol),
                        new("response_keys", byResponseKey.Keys.ToList())
                    ]);
                continue;
            }

            QuoteSnapshot mapped = MapQuote(originalSymbol, quote);
            if (mapped.Last is null && mapped.PreviousClose is null)
            {
                TraceLog.WarnState(
                    "YFinanceNetQuoteProvider",
                    "QuoteResponseNoNumericData",
                    [
                        new("operation_id", operationId),
                        new("original_symbol", originalSymbol),
                        new("response_symbol", quote.Symbol),
                        new("regular_market_price", quote.RegularMarketPrice),
                        new("regular_market_previous_close", quote.RegularMarketPreviousClose),
                        new("regular_market_change", quote.RegularMarketChange)
                    ]);
                continue;
            }

            results[originalSymbol] = mapped;
        }

        TraceLog.InfoState(
            "YFinanceNetQuoteProvider",
            "QuoteBatchMapped",
            [new("operation_id", operationId), new("requested_count", requestedSymbols.Count), new("resolved_count", results.Count), new("symbols", requestedSymbols)]);
        List<string> unresolved = requestedSymbols
            .Where(symbol => !results.ContainsKey(symbol))
            .ToList();
        if (unresolved.Count > 0 && results.Count > 0)
        {
            TraceLog.WarnState(
                "YFinanceNetQuoteProvider",
                "QuoteBatchPartial",
                [new("operation_id", operationId), new("requested_count", requestedSymbols.Count), new("resolved_count", results.Count), new("missing_symbols", unresolved)]);
        }

        TraceLog.InfoState(
            "YFinanceUiBridge",
            "QuoteRequestComplete",
            [new("operation_id", operationId), new("requested_count", requestedSymbols.Count), new("resolved_count", results.Count), new("resolved_symbols", results.Keys.ToList())]);

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

    private static Dictionary<string, string> BuildRequestMap(IEnumerable<string> requestedSymbols)
        => requestedSymbols.Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(
            symbol => symbol,
            YFinanceSymbolMapper.ToRequestSymbol,
            StringComparer.OrdinalIgnoreCase);
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
