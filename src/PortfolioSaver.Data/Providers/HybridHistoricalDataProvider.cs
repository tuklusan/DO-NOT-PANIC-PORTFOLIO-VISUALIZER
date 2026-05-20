using PortfolioSaver.Core.Models;
using PortfolioSaver.Data.Interfaces;
using PortfolioSaver.Data.Services;
using PortfolioSaver.Shared.Diagnostics;
using YFinance.NET.Api;
using YFinanceHistoryResponse = YFinance.NET.Models.HistoryResponse;

namespace PortfolioSaver.Data.Providers;

public sealed class HybridHistoricalDataProvider : IHistoricalDataProvider
{
    private readonly IHistoricalCacheService _cacheService;
    private readonly TimeSpan _cacheFreshness;
    private readonly YFinanceClient _client;

    public HybridHistoricalDataProvider(
        IHistoricalCacheService cacheService,
        HttpClient? httpClient = null,
        string? finnhubApiKey = null,
        string? twelveDataApiKey = null,
        TimeSpan? cacheFreshness = null,
        TimeSpan? minFinnhubSpacing = null,
        TimeSpan? minTwelveDataSpacing = null,
        int rotationSeed = 0,
        IReadOnlyDictionary<string, SymbolProfile>? symbolProfiles = null)
    {
        _cacheService = cacheService;
        _cacheFreshness = cacheFreshness ?? TimeSpan.FromHours(12);
        _client = YFinanceRuntimeClientFactory.GetSharedClient();
    }

    public async Task<IReadOnlyList<TickerHistorySnapshot>> GetHistoryAsync(
        IEnumerable<string> symbols,
        int lookbackDays,
        CancellationToken cancellationToken = default)
    {
        List<string> orderedSymbols = symbols
            .Select(YFinanceSymbolMapper.Normalize)
            .Where(static symbol => !string.IsNullOrWhiteSpace(symbol))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (orderedSymbols.Count == 0)
            return [];

        await _cacheService.PurgeExpiredAsync(cancellationToken).ConfigureAwait(false);

        Dictionary<string, TickerHistorySnapshot> resolved = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, TickerHistorySnapshot> staleCache = new(StringComparer.OrdinalIgnoreCase);
        List<string> pending = [];

        foreach (string symbol in orderedSymbols)
        {
            TickerHistorySnapshot? cached = await _cacheService.LoadAsync(symbol, cancellationToken).ConfigureAwait(false);
            if (cached is not null && cached.LookbackDays == lookbackDays && cached.IsFresh(_cacheFreshness))
            {
                resolved[symbol] = cached;
                continue;
            }

            if (cached is not null)
                staleCache[symbol] = cached;

            pending.Add(symbol);
        }

        if (pending.Count > 0)
        {
            DateTimeOffset endUtc = DateTimeOffset.UtcNow;
            DateTimeOffset startUtc = endUtc.AddDays(-Math.Max(1, lookbackDays));

            foreach (string symbol in pending)
            {
                try
                {
                    string requestSymbol = YFinanceSymbolMapper.ToRequestSymbol(symbol);
                    YFinanceHistoryResponse response = await _client.Ticker(requestSymbol)
                        .GetHistoryResponseAsync(startUtc, endUtc, ResolveInterval(lookbackDays), cancellationToken)
                        .ConfigureAwait(false);

                    TickerHistorySnapshot snapshot = MapHistory(symbol, lookbackDays, response);
                    if (snapshot.Points.Count > 0)
                    {
                        resolved[symbol] = snapshot;
                        await _cacheService.SaveAsync(snapshot, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    TraceLog.WarnState(
                        "YFinanceNetHistoricalProvider",
                        "HistoryFetchFailed",
                        [new("symbol", symbol), new("lookback_days", lookbackDays), new("message", ex.Message)]);
                }
            }
        }

        List<TickerHistorySnapshot> results = [];
        foreach (string symbol in orderedSymbols)
        {
            if (resolved.TryGetValue(symbol, out TickerHistorySnapshot? fetched))
            {
                results.Add(fetched);
                continue;
            }

            if (staleCache.TryGetValue(symbol, out TickerHistorySnapshot? cached))
            {
                results.Add(cached);
                continue;
            }

            results.Add(new TickerHistorySnapshot
            {
                Symbol = symbol,
                LookbackDays = lookbackDays,
                FetchTimestampUtc = DateTimeOffset.UtcNow,
                Points = []
            });
        }

        TraceLog.InfoState(
            "YFinanceNetHistoricalProvider",
            "HistoryBatchComplete",
            [new("requested_count", orderedSymbols.Count), new("resolved_count", resolved.Count), new("lookback_days", lookbackDays)]);

        return results;
    }

    private static TickerHistorySnapshot MapHistory(string originalSymbol, int lookbackDays, YFinanceHistoryResponse response)
    {
        return new TickerHistorySnapshot
        {
            Symbol = originalSymbol,
            LookbackDays = lookbackDays,
            FetchTimestampUtc = DateTimeOffset.UtcNow,
            Points = response.Bars
                .Where(static bar => bar.Close.HasValue)
                .Select(bar => new HistoricalPricePoint
                {
                    TimestampUtc = bar.Timestamp,
                    Close = YFinanceSymbolMapper.NormalizeNumericValue(originalSymbol, bar.Close) ?? 0m
                })
                .Where(static point => point.Close > 0m)
                .OrderBy(point => point.TimestampUtc)
                .ToList()
        };
    }

    private static string ResolveInterval(int lookbackDays)
        => lookbackDays <= 1 ? "1h" : "1d";
}
