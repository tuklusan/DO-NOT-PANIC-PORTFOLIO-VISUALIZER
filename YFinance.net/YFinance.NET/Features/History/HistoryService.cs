using System.Text.Json;
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
    {
        JsonDocument json = await _httpClient.GetJsonAsync(
            $"/v8/finance/chart/{Uri.EscapeDataString(symbol.ToUpperInvariant())}",
            new Dictionary<string, string?>
            {
                ["period1"] = startUtc.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["period2"] = endUtc.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["interval"] = interval,
                ["events"] = "div,splits,capitalGains",
                ["includePrePost"] = "false"
            },
            cancellationToken).ConfigureAwait(false);

        return ParseBars(json.RootElement);
    }

    private static IReadOnlyList<HistoricalBar> ParseBars(JsonElement root)
    {
        if (!root.TryGetProperty("chart", out JsonElement chart) ||
            !chart.TryGetProperty("result", out JsonElement resultArray) ||
            resultArray.ValueKind != JsonValueKind.Array ||
            resultArray.GetArrayLength() == 0)
        {
            return Array.Empty<HistoricalBar>();
        }

        JsonElement result = resultArray[0];
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
            : new List<JsonElement>();

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
}
