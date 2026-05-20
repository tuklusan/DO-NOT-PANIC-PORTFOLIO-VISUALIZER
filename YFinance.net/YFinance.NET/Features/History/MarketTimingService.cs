using System.Globalization;
using System.Text.Json;
using YFinance.NET.Caching;
using YFinance.NET.Diagnostics;
using YFinance.NET.Exceptions;
using YFinance.NET.Transport;
using YFinance.NET.Models;

namespace YFinance.NET.Features.History;

public sealed class MarketTimingService
{
    private readonly YahooFinanceHttpClient _httpClient;
    private readonly PersistentTtlCache<MarketTimingSnapshot> _cache;
    private readonly YFinanceTrace _trace;
    private readonly TimeSpan _minimumCacheTtl;

    public MarketTimingService(
        YahooFinanceHttpClient httpClient,
        string cacheRootPath,
        TimeSpan minimumCacheTtl,
        YFinanceTrace? trace = null)
    {
        _httpClient = httpClient;
        _cache = new PersistentTtlCache<MarketTimingSnapshot>(cacheRootPath);
        _minimumCacheTtl = minimumCacheTtl;
        _trace = trace ?? new YFinanceTrace();
    }

    public async Task<MarketTimingSnapshot?> GetMarketTimingAsync(string symbol, CancellationToken cancellationToken = default)
    {
        string normalized = symbol.Trim().ToUpperInvariant();
        string cacheKey = PersistentTtlCache<MarketTimingSnapshot>.BuildKey("market-timing", normalized);
        MarketTimingSnapshot? cached = await _cache.GetAsync(cacheKey, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            _trace.InfoState("YFinance.MarketTiming", "MarketTimingCacheHit", ("symbol", normalized), ("exchange_local_date", cached.ExchangeLocalDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
            return cached;
        }

        _trace.InfoState("YFinance.MarketTiming", "MarketTimingRequestStart", ("symbol", normalized));
        using JsonDocument json = await _httpClient.GetCachedJsonAsync(
            $"/v8/finance/chart/{Uri.EscapeDataString(normalized)}",
            new Dictionary<string, string?>
            {
                ["range"] = "1d",
                ["interval"] = "1d",
                ["includeTimestamps"] = "false",
                ["includePrePost"] = "true"
            },
            _minimumCacheTtl,
            cancellationToken).ConfigureAwait(false);

        MarketTimingSnapshot? snapshot = ParseMarketTiming(normalized, json.RootElement);
        if (snapshot is null)
        {
            _trace.WarnState("YFinance.MarketTiming", "MarketTimingRequestEmpty", ("symbol", normalized));
            return null;
        }

        TimeSpan ttl = CalculateCacheTtl(snapshot, DateTimeOffset.UtcNow);
        await _cache.SetAsync(cacheKey, snapshot, ttl, cancellationToken).ConfigureAwait(false);
        _trace.InfoState(
            "YFinance.MarketTiming",
            "MarketTimingRequestComplete",
            ("symbol", normalized),
            ("exchange_local_date", snapshot.ExchangeLocalDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            ("cache_ttl_minutes", Math.Round(ttl.TotalMinutes, 2)),
            ("timezone", snapshot.ExchangeTimezoneName ?? "n/a"));
        return snapshot;
    }

    private static MarketTimingSnapshot? ParseMarketTiming(string symbol, JsonElement root)
    {
        if (!root.TryGetProperty("chart", out JsonElement chart))
        {
            throw new YFinanceApiException($"Yahoo chart payload for {symbol} did not contain a chart node.");
        }

        if (chart.TryGetProperty("error", out JsonElement error) &&
            error.ValueKind == JsonValueKind.Object &&
            error.TryGetProperty("description", out JsonElement description))
        {
            throw new YFinanceApiException($"Yahoo market timing request for {symbol} failed: {description.GetString()}");
        }

        if (!chart.TryGetProperty("result", out JsonElement resultArray) ||
            resultArray.ValueKind != JsonValueKind.Array ||
            resultArray.GetArrayLength() == 0)
        {
            return null;
        }

        JsonElement result = resultArray[0];
        HistoryMetadata? metadata = HistoryService.ParseMetadata(symbol, result);
        if (metadata?.CurrentTradingPeriod is null)
        {
            return null;
        }

        DateTimeOffset fetchedUtc = DateTimeOffset.UtcNow;
        DateOnly exchangeLocalDate = ComputeExchangeLocalDate(fetchedUtc, metadata.GmtOffsetSeconds);
        return new MarketTimingSnapshot(
            Symbol: symbol,
            ExchangeName: metadata.ExchangeName,
            ExchangeTimezoneName: metadata.ExchangeTimezoneName,
            InstrumentType: metadata.InstrumentType,
            RegularMarketTimeUtc: metadata.RegularMarketTimeUtc,
            GmtOffsetSeconds: metadata.GmtOffsetSeconds,
            CurrentTradingPeriod: metadata.CurrentTradingPeriod,
            ExchangeLocalDate: exchangeLocalDate,
            FetchedUtc: fetchedUtc,
            RawFields: metadata.RawFields);
    }

    private static DateOnly ComputeExchangeLocalDate(DateTimeOffset utcNow, long? gmtOffsetSeconds)
    {
        if (!gmtOffsetSeconds.HasValue)
        {
            return DateOnly.FromDateTime(utcNow.UtcDateTime);
        }

        DateTimeOffset localNow = utcNow.ToOffset(TimeSpan.FromSeconds(gmtOffsetSeconds.Value));
        return DateOnly.FromDateTime(localNow.DateTime);
    }

    private TimeSpan CalculateCacheTtl(MarketTimingSnapshot snapshot, DateTimeOffset utcNow)
    {
        if (!snapshot.GmtOffsetSeconds.HasValue)
        {
            return _minimumCacheTtl;
        }

        DateTimeOffset localNow = utcNow.ToOffset(TimeSpan.FromSeconds(snapshot.GmtOffsetSeconds.Value));
        DateTimeOffset nextLocalMidnight = new(localNow.Year, localNow.Month, localNow.Day, 0, 0, 0, localNow.Offset);
        nextLocalMidnight = nextLocalMidnight.AddDays(1).AddMinutes(5);
        TimeSpan ttl = nextLocalMidnight.ToUniversalTime() - utcNow;
        if (ttl < _minimumCacheTtl)
        {
            return _minimumCacheTtl;
        }

        TimeSpan maximumTtl = TimeSpan.FromHours(36);
        return ttl > maximumTtl ? maximumTtl : ttl;
    }
}
