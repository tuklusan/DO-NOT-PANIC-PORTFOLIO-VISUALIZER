// ============================================================================
// Copyright (c) 2026 Supratim Sanyal of SANYALnet Labs.
// Proprietary rights reserved except as expressly licensed herein.
//
// DO NOT PANIC PORTFOLIO VIEWER
// This software and its derivatives are licensed for STRICTLY NON-COMMERCIAL,
// personal, educational, or hobbyist use only. Commercial exploitation,
// corporate internal operations, or AI model training are strictly forbidden.
//
// ATTRIBUTION & DEPENDENCIES: This application incorporates the YFinance library,
// which is licensed under the Apache License, Version 2.0. A copy of the Apache
// License is provided within the distribution environment.
//
// FINANCIAL DISCLAIMER: This software is a passive visualization tool only.
// It does not provide financial, investment, legal, or tax advice. All data
// calculation and scraping outputs are provided 'AS IS' with zero guarantee
// of real-time accuracy or upstream availability.
//
// This file is subject to the terms and conditions defined in the LICENSE
// file located in the root directory of this source code repository.
// Removal or modification of this legal notice constitutes copyright infringement.
// ============================================================================
using System.Text.Json;
using YFinance.NET.Config;
using YFinance.NET.Diagnostics;
using YFinance.NET.Exceptions;
using YFinance.NET.Features.Quotes;
using YFinance.NET.Models;
using YFinance.NET.Transport;

namespace YFinance.NET.Features.History;

public sealed class HistoryService
{
    private readonly YahooFinanceHttpClient _httpClient;
    private readonly YFinanceOptions _options;
    private readonly TimeSpan _cacheTtl;
    private readonly YFinanceTrace _trace;

    public HistoryService(YahooFinanceHttpClient httpClient, YFinanceOptions options, YFinanceTrace? trace = null)
        : this(httpClient, options, options.DefaultCacheTtl, trace)
    {
    }

    [Obsolete("Use HistoryService(YahooFinanceHttpClient, YFinanceOptions, YFinanceTrace?). This legacy overload preserves only cacheTtl and old no-locale-query behavior; all other YFinanceOptions fields use defaults.")]
    public HistoryService(YahooFinanceHttpClient httpClient, TimeSpan cacheTtl, YFinanceTrace? trace = null)
        : this(httpClient, new YFinanceOptions { DefaultCacheTtl = cacheTtl, Language = string.Empty, Region = string.Empty }, cacheTtl, trace)
    {
    }

    private HistoryService(YahooFinanceHttpClient httpClient, YFinanceOptions options, TimeSpan cacheTtl, YFinanceTrace? trace)
    {
        _httpClient = httpClient;
        _options = options;
        _cacheTtl = cacheTtl;
        _trace = trace ?? new YFinanceTrace();
    }

    public async Task<IReadOnlyList<HistoricalBar>> GetHistoryAsync(string symbol, DateTimeOffset startUtc, DateTimeOffset endUtc, string interval = "1d", CancellationToken cancellationToken = default)
        => (await GetHistoryResponseAsync(symbol, startUtc, endUtc, interval, cancellationToken).ConfigureAwait(false)).Bars;

    public async Task<HistoryResponse> GetHistoryResponseAsync(string symbol, DateTimeOffset startUtc, DateTimeOffset endUtc, string interval = "1d", CancellationToken cancellationToken = default)
    {
        string normalized = symbol.Trim().ToUpperInvariant();
        _trace.InfoState("YFinance.History", "HistoryRequestStart", ("symbol", normalized), ("start_utc", startUtc), ("end_utc", endUtc), ("interval", interval));
        Dictionary<string, string?> query = new()
        {
            ["period1"] = startUtc.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["period2"] = endUtc.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["interval"] = interval,
            ["events"] = "div,splits,capitalGains",
            ["includePrePost"] = "false"
        };
        _options.AddLocaleQueryParameters(query);

        JsonDocument json = await _httpClient.GetCachedJsonAsync(
            $"/v8/finance/chart/{Uri.EscapeDataString(normalized)}",
            query,
            _cacheTtl,
            cancellationToken).ConfigureAwait(false);

        if (!TryGetChartObject(json.RootElement, out _))
        {
            _trace.WarnState("YFinance.History", "HistoryChartPayloadMalformed", ("symbol", normalized), ("chart_state", DescribeChartPayload(json.RootElement)));
        }

        HistoryResponse response = ParseHistoryResponse(normalized, endUtc, json.RootElement);
        _trace.InfoState("YFinance.History", "HistoryRequestComplete", ("symbol", normalized), ("bar_count", response.Bars.Count), ("timezone", response.Metadata?.ExchangeTimezoneName ?? "n/a"), ("granularity", response.Metadata?.DataGranularity ?? "n/a"));
        return response;
    }

    internal static HistoryResponse ParseHistoryResponse(string symbol, DateTimeOffset? endUtc, JsonElement root)
    {
        if (!root.TryGetProperty("chart", out JsonElement chart))
        {
            throw new YFinanceApiException($"Yahoo chart payload for {symbol} did not contain a chart node.");
        }

        if (chart.ValueKind == JsonValueKind.Null)
        {
            return new HistoryResponse(symbol, Array.Empty<HistoricalBar>(), null);
        }

        if (chart.ValueKind != JsonValueKind.Object)
        {
            throw new YFinanceApiException($"Yahoo chart payload for {symbol} contained a {chart.ValueKind} chart node instead of an object.");
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

    internal static bool TryGetChartObject(JsonElement root, out JsonElement chart)
        => root.TryGetProperty("chart", out chart) && chart.ValueKind == JsonValueKind.Object;

    internal static string DescribeChartPayload(JsonElement root)
    {
        if (!root.TryGetProperty("chart", out JsonElement chart))
        {
            return "Absent";
        }

        return chart.ValueKind.ToString();
    }

    internal static HistoryMetadata? ParseMetadata(string symbol, JsonElement result)
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
            RegularMarketTimeUtc: GetUnixDateTimeOffset(meta, "regularMarketTime"),
            PriceHint: (int?)QuoteService.GetLong(meta, "priceHint"),
            GmtOffsetSeconds: QuoteService.GetLong(meta, "gmtoffset"),
            CurrentTradingPeriod: ParseCurrentTradingPeriods(meta),
            ValidRanges: validRanges,
            RawFields: fields);
    }

    internal static CurrentTradingPeriods? ParseCurrentTradingPeriods(JsonElement meta)
    {
        if (!meta.TryGetProperty("currentTradingPeriod", out JsonElement periods) || periods.ValueKind != JsonValueKind.Object)
            return null;

        TradingPeriodWindow? pre = ParseTradingPeriod(periods, "pre");
        TradingPeriodWindow? regular = ParseTradingPeriod(periods, "regular");
        TradingPeriodWindow? post = ParseTradingPeriod(periods, "post");
        if (pre is null && regular is null && post is null)
            return null;

        return new CurrentTradingPeriods(pre, regular, post);
    }

    internal static TradingPeriodWindow? ParseTradingPeriod(JsonElement periods, string propertyName)
    {
        if (!periods.TryGetProperty(propertyName, out JsonElement period) || period.ValueKind != JsonValueKind.Object)
            return null;

        long? start = QuoteService.GetLong(period, "start");
        long? end = QuoteService.GetLong(period, "end");
        if (!start.HasValue || !end.HasValue)
            return null;

        return new TradingPeriodWindow(
            StartUtc: DateTimeOffset.FromUnixTimeSeconds(start.Value),
            EndUtc: DateTimeOffset.FromUnixTimeSeconds(end.Value),
            Timezone: QuoteService.GetString(period, "timezone"),
            GmtOffsetSeconds: QuoteService.GetLong(period, "gmtoffset"));
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

    internal static object? ConvertScalar(JsonElement value)
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

    private static DateTimeOffset? GetUnixDateTimeOffset(JsonElement item, string propertyName)
    {
        long? value = QuoteService.GetLong(item, propertyName);
        return value.HasValue ? DateTimeOffset.FromUnixTimeSeconds(value.Value) : null;
    }
}
