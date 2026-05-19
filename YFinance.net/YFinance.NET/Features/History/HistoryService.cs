using System.Text.Json;
using YFinance.NET.Exceptions;
using YFinance.NET.Features.Quotes;
using YFinance.NET.Models;
using YFinance.NET.Transport;

namespace YFinance.NET.Features.History;

public sealed class HistoryService
{
    private readonly YahooFinanceHttpClient _httpClient;

    public HistoryService(YahooFinanceHttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<HistoricalBar>> GetHistoryAsync(string symbol, DateTimeOffset startUtc, DateTimeOffset endUtc, string interval = "1d", CancellationToken cancellationToken = default)
        => (await GetHistoryResponseAsync(symbol, startUtc, endUtc, interval, cancellationToken).ConfigureAwait(false)).Bars;

    public async Task<HistoryResponse> GetHistoryResponseAsync(string symbol, DateTimeOffset startUtc, DateTimeOffset endUtc, string interval = "1d", CancellationToken cancellationToken = default)
    {
        string normalized = symbol.Trim().ToUpperInvariant();
        JsonDocument json = await _httpClient.GetJsonAsync(
            $"/v8/finance/chart/{Uri.EscapeDataString(normalized)}",
            new Dictionary<string, string?>
            {
                ["period1"] = startUtc.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["period2"] = endUtc.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["interval"] = interval,
                ["events"] = "div,splits,capitalGains",
                ["includePrePost"] = "false"
            },
            cancellationToken).ConfigureAwait(false);

        return ParseHistoryResponse(normalized, endUtc, json.RootElement);
    }

    private static HistoryResponse ParseHistoryResponse(string symbol, DateTimeOffset? endUtc, JsonElement root)
    {
        if (!root.TryGetProperty("chart", out JsonElement chart))
        {
            throw new YFinanceApiException($"Yahoo chart payload for {symbol} did not contain a chart node.");
        }

        if (chart.TryGetProperty("error", out JsonElement error) &&
            error.ValueKind == JsonValueKind.Object &&
            error.TryGetProperty("description", out JsonElement description))
        {
            throw new YFinanceApiException($"Yahoo chart request for {symbol} failed: {description.GetString()}");
        }

        if (!chart.TryGetProperty("result", out JsonElement resultArray) ||
            resultArray.ValueKind != JsonValueKind.Array ||
            resultArray.GetArrayLength() == 0)
        {
            return new HistoryResponse(symbol, Array.Empty<HistoricalBar>(), null);
        }

        JsonElement result = resultArray[0];
        HistoryMetadata? metadata = ParseMetadata(symbol, result);
        IReadOnlyList<HistoricalBar> bars = ParseBars(result, endUtc);
        return new HistoryResponse(symbol, bars, metadata);
    }

    private static HistoryMetadata? ParseMetadata(string symbol, JsonElement result)
    {
        if (!result.TryGetProperty("meta", out JsonElement meta) || meta.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        Dictionary<string, object?> fields = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty property in meta.EnumerateObject())
        {
            fields[property.Name] = ConvertScalar(property.Value);
        }

        IReadOnlyList<string> validRanges = meta.TryGetProperty("validRanges", out JsonElement validRangesValue) && validRangesValue.ValueKind == JsonValueKind.Array
            ? validRangesValue.EnumerateArray()
                             .Where(static item => item.ValueKind == JsonValueKind.String)
                             .Select(static item => item.GetString()!)
                             .ToArray()
            : Array.Empty<string>();

        return new HistoryMetadata(
            Symbol: symbol,
            Currency: QuoteService.GetString(meta, "currency"),
            ExchangeName: QuoteService.GetString(meta, "exchangeName"),
            ExchangeTimezoneName: QuoteService.GetString(meta, "exchangeTimezoneName"),
            InstrumentType: QuoteService.GetString(meta, "instrumentType"),
            DataGranularity: QuoteService.GetString(meta, "dataGranularity"),
            RegularMarketPrice: QuoteService.GetDecimal(meta, "regularMarketPrice"),
            PriceHint: (int?)QuoteService.GetLong(meta, "priceHint"),
            GmtOffsetSeconds: QuoteService.GetLong(meta, "gmtoffset"),
            ValidRanges: validRanges,
            RawFields: fields);
    }

    private static IReadOnlyList<HistoricalBar> ParseBars(JsonElement result, DateTimeOffset? endUtc)
    {
        if (!result.TryGetProperty("timestamp", out JsonElement timestamps) ||
            !result.TryGetProperty("indicators", out JsonElement indicators) ||
            !indicators.TryGetProperty("quote", out JsonElement quoteArray) ||
            quoteArray.ValueKind != JsonValueKind.Array ||
            quoteArray.GetArrayLength() == 0)
        {
            return Array.Empty<HistoricalBar>();
        }

        JsonElement quote = quoteArray[0];
        List<JsonElement> open = GetArray(quote, "open");
        List<JsonElement> high = GetArray(quote, "high");
        List<JsonElement> low = GetArray(quote, "low");
        List<JsonElement> close = GetArray(quote, "close");
        List<JsonElement> volume = GetArray(quote, "volume");

        List<HistoricalBar> bars = new();
        int index = 0;
        foreach (JsonElement timestamp in timestamps.EnumerateArray())
        {
            DateTimeOffset ts = DateTimeOffset.FromUnixTimeSeconds(timestamp.GetInt64());
            if (endUtc.HasValue && ts >= endUtc.Value)
            {
                index++;
                continue;
            }

            bars.Add(new HistoricalBar(
                ts,
                GetDecimal(open, index),
                GetDecimal(high, index),
                GetDecimal(low, index),
                GetDecimal(close, index),
                GetLong(volume, index)));
            index++;
        }

        return bars;
    }

    private static List<JsonElement> GetArray(JsonElement parent, string propertyName)
        => parent.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(static element => element.Clone()).ToList()
            : [];

    private static decimal? GetDecimal(IReadOnlyList<JsonElement> items, int index)
    {
        if (index >= items.Count) return null;
        JsonElement value = items[index];
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out decimal number)) return number;
        return null;
    }

    private static long? GetLong(IReadOnlyList<JsonElement> items, int index)
    {
        if (index >= items.Count) return null;
        JsonElement value = items[index];
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number)) return number;
        return null;
    }

    private static object? ConvertScalar(JsonElement value)
        => value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number when value.TryGetInt64(out long l) => l,
            JsonValueKind.Number when value.TryGetDecimal(out decimal d) => d,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Array => value.EnumerateArray().Select(ConvertScalar).ToArray(),
            JsonValueKind.Object => value.EnumerateObject().ToDictionary(static property => property.Name, static property => ConvertScalar(property.Value), StringComparer.OrdinalIgnoreCase),
            _ => null
        };
}
