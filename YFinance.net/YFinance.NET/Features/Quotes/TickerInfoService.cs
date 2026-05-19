using System.Text.Json;
using YFinance.NET.Caching;
using YFinance.NET.Config;
using YFinance.NET.Models;

namespace YFinance.NET.Features.Quotes;

public sealed class TickerInfoService
{
    private static readonly string[] DefaultInfoModules = ["financialData", "quoteType", "defaultKeyStatistics", "assetProfile", "summaryDetail"];

    // Preserve a clear "quote + quoteSummary => normalized info" seam so upstream
    // quote.py changes can be re-ported without rediscovering the whole design.
    private readonly QuoteService _quoteService;
    private readonly QuoteSummaryService _quoteSummaryService;
    private readonly PersistentTtlCache<TickerInfo> _persistentCache;
    private readonly YFinanceOptions _options;

    public TickerInfoService(QuoteService quoteService, QuoteSummaryService quoteSummaryService, YFinanceOptions options)
    {
        _quoteService = quoteService;
        _quoteSummaryService = quoteSummaryService;
        _options = options;
        _persistentCache = new PersistentTtlCache<TickerInfo>(options.MetadataCacheDirectoryPath);
    }

    public async Task<TickerInfo?> GetInfoAsync(string symbol, CancellationToken cancellationToken = default)
    {
        string normalized = symbol.Trim().ToUpperInvariant();
        string cacheKey = PersistentTtlCache<TickerInfo>.BuildKey(CacheBuckets.Metadata, normalized, "info");
        TickerInfo? cached = await _persistentCache.GetAsync(cacheKey, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            return cached;
        }

        QuoteSnapshot? quote = await _quoteService.GetQuoteAsync(normalized, cancellationToken).ConfigureAwait(false);
        QuoteSummaryResult? summary = await _quoteSummaryService.GetSummaryAsync(normalized, DefaultInfoModules, cancellationToken).ConfigureAwait(false);
        TickerInfo? info = Normalize(normalized, quote, summary);
        if (info is not null)
        {
            await _persistentCache.SetAsync(cacheKey, info, _options.PersistentMetadataCacheTtl, cancellationToken).ConfigureAwait(false);
        }

        return info;
    }

    public async Task<IReadOnlyDictionary<string, TickerInfo?>> GetInfosAsync(IEnumerable<string> symbols, CancellationToken cancellationToken = default)
    {
        string[] normalized = symbols.Select(static symbol => symbol.Trim().ToUpperInvariant())
                                     .Where(static symbol => !string.IsNullOrWhiteSpace(symbol))
                                     .Distinct(StringComparer.Ordinal)
                                     .ToArray();
        if (normalized.Length == 0)
        {
            return new Dictionary<string, TickerInfo?>(StringComparer.Ordinal);
        }

        Dictionary<string, TickerInfo?> results = new(StringComparer.OrdinalIgnoreCase);
        List<string> unresolved = new();
        foreach (string symbol in normalized)
        {
            string cacheKey = PersistentTtlCache<TickerInfo>.BuildKey(CacheBuckets.Metadata, symbol, "info");
            TickerInfo? cached = await _persistentCache.GetAsync(cacheKey, cancellationToken).ConfigureAwait(false);
            if (cached is not null)
            {
                results[symbol] = cached;
            }
            else
            {
                unresolved.Add(symbol);
            }
        }

        if (unresolved.Count == 0)
        {
            return results;
        }

        IReadOnlyDictionary<string, QuoteSnapshot> quotes = await _quoteService.GetQuotesAsync(unresolved, cancellationToken).ConfigureAwait(false);
        foreach (string symbol in unresolved)
        {
            QuoteSummaryResult? summary = await _quoteSummaryService.GetSummaryAsync(symbol, DefaultInfoModules, cancellationToken).ConfigureAwait(false);
            quotes.TryGetValue(symbol, out QuoteSnapshot? quote);
            TickerInfo? info = Normalize(symbol, quote, summary);
            results[symbol] = info;
            if (info is not null)
            {
                string cacheKey = PersistentTtlCache<TickerInfo>.BuildKey(CacheBuckets.Metadata, symbol, "info");
                await _persistentCache.SetAsync(cacheKey, info, _options.PersistentMetadataCacheTtl, cancellationToken).ConfigureAwait(false);
            }
        }

        return results;
    }

    private static TickerInfo? Normalize(string symbol, QuoteSnapshot? quote, QuoteSummaryResult? summary)
    {
        if (quote is null && summary is null)
        {
            return null;
        }

        Dictionary<string, object?> fields = new(StringComparer.OrdinalIgnoreCase);
        if (summary is not null)
        {
            foreach ((string moduleName, JsonElement module) in summary.Modules)
            {
                Flatten(moduleName, module, fields);
            }
        }

        string normalizedSymbol = symbol.ToUpperInvariant();
        fields["symbol"] = normalizedSymbol;
        MergeQuote(fields, quote);

        return new TickerInfo(
            Symbol: normalizedSymbol,
            ShortName: GetString(fields, "shortName") ?? quote?.ShortName,
            LongName: GetString(fields, "longName") ?? quote?.LongName,
            DisplayName: GetString(fields, "displayName") ?? quote?.DisplayName,
            Currency: GetString(fields, "currency") ?? quote?.Currency,
            Exchange: GetString(fields, "fullExchangeName") ?? GetString(fields, "exchange") ?? quote?.Exchange,
            ExchangeTimezoneName: GetString(fields, "exchangeTimezoneName") ?? quote?.ExchangeTimezoneName,
            ExchangeTimezoneShortName: GetString(fields, "exchangeTimezoneShortName") ?? quote?.ExchangeTimezoneShortName,
            QuoteType: GetString(fields, "quoteType") ?? quote?.QuoteType,
            MarketState: GetString(fields, "marketState") ?? quote?.MarketState,
            RegularMarketPrice: GetDecimal(fields, "regularMarketPrice") ?? quote?.RegularMarketPrice,
            RegularMarketPreviousClose: GetDecimal(fields, "regularMarketPreviousClose") ?? quote?.RegularMarketPreviousClose,
            RegularMarketOpen: GetDecimal(fields, "regularMarketOpen") ?? quote?.RegularMarketOpen,
            RegularMarketDayHigh: GetDecimal(fields, "regularMarketDayHigh") ?? quote?.RegularMarketDayHigh,
            RegularMarketDayLow: GetDecimal(fields, "regularMarketDayLow") ?? quote?.RegularMarketDayLow,
            RegularMarketChange: GetDecimal(fields, "regularMarketChange") ?? quote?.RegularMarketChange,
            RegularMarketChangePercent: GetDecimal(fields, "regularMarketChangePercent") ?? quote?.RegularMarketChangePercent,
            FiftyTwoWeekLow: GetDecimal(fields, "fiftyTwoWeekLow") ?? quote?.FiftyTwoWeekLow,
            FiftyTwoWeekHigh: GetDecimal(fields, "fiftyTwoWeekHigh") ?? quote?.FiftyTwoWeekHigh,
            FiftyDayAverage: GetDecimal(fields, "fiftyDayAverage") ?? quote?.FiftyDayAverage,
            TwoHundredDayAverage: GetDecimal(fields, "twoHundredDayAverage") ?? quote?.TwoHundredDayAverage,
            RegularMarketVolume: GetLong(fields, "regularMarketVolume") ?? quote?.RegularMarketVolume,
            AverageVolume: GetLong(fields, "averageVolume") ?? quote?.AverageVolume,
            AverageVolume10Day: GetLong(fields, "averageDailyVolume10Day") ?? GetLong(fields, "averageVolume10days") ?? quote?.AverageVolume10Day,
            SharesOutstanding: GetLong(fields, "sharesOutstanding") ?? quote?.SharesOutstanding,
            MarketCap: GetLong(fields, "marketCap") ?? quote?.MarketCap,
            TrailingPe: GetDecimal(fields, "trailingPE") ?? quote?.TrailingPe,
            ForwardPe: GetDecimal(fields, "forwardPE") ?? quote?.ForwardPe,
            DividendYield: GetDecimal(fields, "dividendYield"),
            Sector: GetString(fields, "sector"),
            Industry: GetString(fields, "industry"),
            LongBusinessSummary: GetString(fields, "longBusinessSummary"),
            Website: GetString(fields, "website"),
            FlatFields: fields);
    }

    private static void MergeQuote(IDictionary<string, object?> fields, QuoteSnapshot? quote)
    {
        if (quote is null)
        {
            return;
        }

        SetIfMissing(fields, "shortName", quote.ShortName);
        SetIfMissing(fields, "longName", quote.LongName);
        SetIfMissing(fields, "displayName", quote.DisplayName);
        SetIfMissing(fields, "currency", quote.Currency);
        SetIfMissing(fields, "fullExchangeName", quote.Exchange);
        SetIfMissing(fields, "exchangeTimezoneName", quote.ExchangeTimezoneName);
        SetIfMissing(fields, "exchangeTimezoneShortName", quote.ExchangeTimezoneShortName);
        SetIfMissing(fields, "quoteType", quote.QuoteType);
        SetIfMissing(fields, "marketState", quote.MarketState);
        SetIfMissing(fields, "regularMarketPrice", quote.RegularMarketPrice);
        SetIfMissing(fields, "regularMarketPreviousClose", quote.RegularMarketPreviousClose);
        SetIfMissing(fields, "regularMarketOpen", quote.RegularMarketOpen);
        SetIfMissing(fields, "regularMarketDayHigh", quote.RegularMarketDayHigh);
        SetIfMissing(fields, "regularMarketDayLow", quote.RegularMarketDayLow);
        SetIfMissing(fields, "regularMarketChange", quote.RegularMarketChange);
        SetIfMissing(fields, "regularMarketChangePercent", quote.RegularMarketChangePercent);
        SetIfMissing(fields, "fiftyTwoWeekLow", quote.FiftyTwoWeekLow);
        SetIfMissing(fields, "fiftyTwoWeekHigh", quote.FiftyTwoWeekHigh);
        SetIfMissing(fields, "fiftyDayAverage", quote.FiftyDayAverage);
        SetIfMissing(fields, "twoHundredDayAverage", quote.TwoHundredDayAverage);
        SetIfMissing(fields, "regularMarketVolume", quote.RegularMarketVolume);
        SetIfMissing(fields, "averageVolume", quote.AverageVolume);
        SetIfMissing(fields, "averageDailyVolume10Day", quote.AverageVolume10Day);
        SetIfMissing(fields, "sharesOutstanding", quote.SharesOutstanding);
        SetIfMissing(fields, "marketCap", quote.MarketCap);
        SetIfMissing(fields, "trailingPE", quote.TrailingPe);
        SetIfMissing(fields, "forwardPE", quote.ForwardPe);
    }

    private static void SetIfMissing(IDictionary<string, object?> fields, string key, object? value)
    {
        if (value is null || fields.ContainsKey(key))
        {
            return;
        }

        fields[key] = value;
    }

    private static void Flatten(string? key, JsonElement element, IDictionary<string, object?> fields)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (element.TryGetProperty("raw", out JsonElement raw))
                {
                    fields[key ?? string.Empty] = ConvertScalar(key, raw);
                    return;
                }

                foreach (JsonProperty property in element.EnumerateObject())
                {
                    Flatten(property.Name, property.Value, fields);
                }
                break;
            case JsonValueKind.Array:
                fields[key ?? string.Empty] = element.EnumerateArray().Select(static value => value.ValueKind == JsonValueKind.Object && value.TryGetProperty("raw", out JsonElement rawValue)
                    ? ConvertScalar(null, rawValue)
                    : ConvertScalar(null, value)).ToArray();
                break;
            default:
                fields[key ?? string.Empty] = ConvertScalar(key, element);
                break;
        }
    }

    private static object? ConvertScalar(string? key, JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()?.Replace('\u00A0', ' '),
            JsonValueKind.Number when value.TryGetInt64(out long l) => l,
            JsonValueKind.Number when value.TryGetDecimal(out decimal d) => d,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => value.EnumerateArray().Select(static element => ConvertScalar(null, element)).ToArray(),
            JsonValueKind.Object => value.EnumerateObject().ToDictionary(static property => property.Name, static property => ConvertScalar(property.Name, property.Value), StringComparer.OrdinalIgnoreCase),
            _ => value.ToString()
        };
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> fields, string key)
        => fields.TryGetValue(key, out object? value) ? value?.ToString() : null;

    private static decimal? GetDecimal(IReadOnlyDictionary<string, object?> fields, string key)
    {
        if (!fields.TryGetValue(key, out object? value) || value is null)
        {
            return null;
        }

        return value switch
        {
            decimal d => d,
            double dbl => Convert.ToDecimal(dbl, System.Globalization.CultureInfo.InvariantCulture),
            float f => Convert.ToDecimal(f, System.Globalization.CultureInfo.InvariantCulture),
            long l => l,
            int i => i,
            string s when decimal.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal parsed) => parsed,
            _ => null
        };
    }

    private static long? GetLong(IReadOnlyDictionary<string, object?> fields, string key)
    {
        if (!fields.TryGetValue(key, out object? value) || value is null)
        {
            return null;
        }

        return value switch
        {
            long l => l,
            int i => i,
            decimal d => decimal.ToInt64(decimal.Truncate(d)),
            double dbl => Convert.ToInt64(Math.Truncate(dbl), System.Globalization.CultureInfo.InvariantCulture),
            string s when long.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out long parsed) => parsed,
            _ => null
        };
    }
}
