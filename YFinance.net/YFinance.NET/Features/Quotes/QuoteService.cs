// ============================================================================
// Copyright (c) 2026 Supratim Sanyal of SANYALnet Labs.
// Proprietary rights reserved except as expressly licensed herein.
//
// DO NOT PANIC PORTFOLIO VISUALIZER
// This file is governed by the SANYALnet Labs Non-Commercial License in the
// root LICENSE file. Non-Commercial use is permitted; Commercial Use and use
// for AI/ML model training are prohibited unless separately authorized.
//
// Attribution is required: "Based on original work by Supratim Sanyal of
// SANYALnet Labs." See LICENSE for full terms, warranty disclaimer, termination,
// patent, trademark, and governing-law provisions.
// ============================================================================
using System.Text.Json;
using YFinance.NET.Config;
using YFinance.NET.Diagnostics;
using YFinance.NET.Models;
using YFinance.NET.Transport;

namespace YFinance.NET.Features.Quotes;

public sealed class QuoteService
{
    private readonly YahooFinanceHttpClient _httpClient;
    private readonly YFinanceOptions _options;
    private readonly YFinanceTrace _trace;

    public QuoteService(YahooFinanceHttpClient httpClient, YFinanceOptions options, YFinanceTrace? trace = null)
    {
        _httpClient = httpClient;
        _options = options;
        _trace = trace ?? new YFinanceTrace(options.TraceSink);
    }

    public async Task<QuoteSnapshot?> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<string, QuoteSnapshot> results = await GetQuotesAsync([symbol], cancellationToken).ConfigureAwait(false);
        return results.TryGetValue(symbol.Trim().ToUpperInvariant(), out QuoteSnapshot? snapshot) ? snapshot : null;
    }

    public async Task<IReadOnlyDictionary<string, QuoteSnapshot>> GetQuotesAsync(IEnumerable<string> symbols, CancellationToken cancellationToken = default)
    {
        string[] normalized = symbols.Select(static symbol => symbol.Trim().ToUpperInvariant())
                                     .Where(static symbol => !string.IsNullOrWhiteSpace(symbol))
                                     .Distinct(StringComparer.Ordinal)
                                     .ToArray();
        if (normalized.Length == 0)
        {
            return new Dictionary<string, QuoteSnapshot>(StringComparer.Ordinal);
        }

        Dictionary<string, QuoteSnapshot> results = new(StringComparer.OrdinalIgnoreCase);
        _trace.InfoState("YFinance.Quotes", "QuoteBatchStart", ("symbol_count", normalized.Length), ("max_symbols_per_request", _options.MaxSymbolsPerQuoteRequest));
        foreach (string[] batch in Chunk(normalized, Math.Max(1, _options.MaxSymbolsPerQuoteRequest)))
        {
            _trace.InfoState("YFinance.Quotes", "QuoteBatchRequest", ("symbols", batch), ("batch_size", batch.Length));
            Dictionary<string, string?> query = new()
            {
                ["symbols"] = string.Join(',', batch),
                ["formatted"] = "false"
            };
            _options.AddLocaleQueryParameters(query);

            JsonDocument json = await _httpClient.GetCachedJsonAsync(
                "/v7/finance/quote",
                query,
                _options.DefaultCacheTtl,
                cancellationToken).ConfigureAwait(false);

            JsonElement root = json.RootElement;
            if (!root.TryGetProperty("quoteResponse", out JsonElement quoteResponse) ||
                !quoteResponse.TryGetProperty("result", out JsonElement resultArray))
            {
                continue;
            }

            foreach (JsonElement item in resultArray.EnumerateArray())
            {
                string? symbol = GetString(item, "symbol");
                if (string.IsNullOrWhiteSpace(symbol))
                {
                    continue;
                }

                results[symbol.ToUpperInvariant()] = CreateSnapshot(item, symbol);
            }
        }

        _trace.InfoState("YFinance.Quotes", "QuoteBatchComplete", ("requested_count", normalized.Length), ("resolved_count", results.Count));
        return results;
    }

    internal static QuoteSnapshot CreateSnapshot(JsonElement item, string symbol)
    {
        return new QuoteSnapshot(
            Symbol: symbol.ToUpperInvariant(),
            ShortName: GetString(item, "shortName"),
            LongName: GetString(item, "longName"),
            DisplayName: GetString(item, "displayName"),
            Currency: GetString(item, "currency"),
            Exchange: GetString(item, "fullExchangeName") ?? GetString(item, "exchange"),
            ExchangeTimezoneName: GetString(item, "exchangeTimezoneName"),
            ExchangeTimezoneShortName: GetString(item, "exchangeTimezoneShortName"),
            QuoteType: GetString(item, "quoteType"),
            MarketState: GetString(item, "marketState"),
            RegularMarketPrice: GetDecimal(item, "regularMarketPrice"),
            RegularMarketPreviousClose: GetDecimal(item, "regularMarketPreviousClose"),
            RegularMarketOpen: GetDecimal(item, "regularMarketOpen"),
            RegularMarketDayHigh: GetDecimal(item, "regularMarketDayHigh"),
            RegularMarketDayLow: GetDecimal(item, "regularMarketDayLow"),
            RegularMarketChange: GetDecimal(item, "regularMarketChange"),
            RegularMarketChangePercent: GetDecimal(item, "regularMarketChangePercent"),
            FiftyTwoWeekLow: GetDecimal(item, "fiftyTwoWeekLow"),
            FiftyTwoWeekHigh: GetDecimal(item, "fiftyTwoWeekHigh"),
            FiftyDayAverage: GetDecimal(item, "fiftyDayAverage"),
            TwoHundredDayAverage: GetDecimal(item, "twoHundredDayAverage"),
            RegularMarketVolume: GetLong(item, "regularMarketVolume"),
            AverageVolume: GetLong(item, "averageVolume"),
            AverageVolume10Day: GetLong(item, "averageDailyVolume10Day") ?? GetLong(item, "averageVolume10days"),
            SharesOutstanding: GetLong(item, "sharesOutstanding"),
            MarketCap: GetLong(item, "marketCap"),
            TrailingPe: GetDecimal(item, "trailingPE"),
            ForwardPe: GetDecimal(item, "forwardPE"),
            Raw: item.Clone());
    }

    internal static string? GetString(JsonElement item, string propertyName)
        => item.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    internal static decimal? GetDecimal(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out JsonElement value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out decimal result)) return result;
        if (value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out result)) return result;
        return null;
    }

    internal static long? GetLong(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out JsonElement value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long result)) return result;
        if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out result)) return result;
        return null;
    }

    private static IEnumerable<string[]> Chunk(string[] values, int chunkSize)
    {
        for (int index = 0; index < values.Length; index += chunkSize)
        {
            int count = Math.Min(chunkSize, values.Length - index);
            string[] batch = new string[count];
            Array.Copy(values, index, batch, 0, count);
            yield return batch;
        }
    }
}
