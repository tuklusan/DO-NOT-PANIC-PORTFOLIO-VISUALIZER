using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using YFinance.NET.Api;
using YFinance.NET.Config;
using YFinance.NET.Diagnostics;
using YFinance.NET.Models;
using PortfolioSaver.Shared.Diagnostics;

namespace YFinance.NET.Exerciser;

internal static partial class Program
{
    private const string WikipediaUrl = "https://en.wikipedia.org/wiki/List_of_S%26P_500_companies";
    private const string CacheFileName = "sp500-top100-cache.json";
    private const int DefaultCacheTtlMinutes = 10;
    private const int DefaultTotalCycles = 5;
    private const int DefaultTopCount = 100;
    private const int DefaultHistoryWarmupCount = 12;
    private const int DefaultHistoryLookbackDays = 5;
    private static readonly TimeSpan DefaultRefreshInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DefaultPerTickerDelay = TimeSpan.FromSeconds(1);

    private static async Task Main(string[] args)
    {
        RunnerOptions runnerOptions = RunnerOptions.Parse(args);
        YFinanceOptions options = new()
        {
            MinimumRequestSpacing = runnerOptions.PerTickerDelay,
            MaxRetries = 3,
            DefaultCacheTtl = TimeSpan.FromMinutes(10),
            SummaryCacheTtl = TimeSpan.FromMinutes(10),
            PersistentMetadataCacheTtl = TimeSpan.FromMinutes(Math.Max(1, runnerOptions.CacheTtlMinutes)),
            MaxSymbolsPerQuoteRequest = 25,
            TraceSink = new PortfolioSaverTraceSink()
        };

        TraceLog.InfoState("YFinance.Exerciser", "ExerciserStart",
            [new("top_count", runnerOptions.TopCount),
             new("total_cycles", runnerOptions.TotalCycles),
             new("refresh_interval", runnerOptions.RefreshInterval),
             new("per_ticker_delay", runnerOptions.PerTickerDelay),
             new("history_warmup_count", runnerOptions.HistoryWarmupCount),
             new("symbol_override_count", runnerOptions.SymbolOverrides.Count)]);
        using YFinanceClient client = new(options);
        string cachePath = Path.Combine(AppContext.BaseDirectory, CacheFileName);
        List<string> tickers = await ResolveTickersAsync(client, cachePath, runnerOptions, CancellationToken.None);
        Console.WriteLine($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} Loaded top {tickers.Count} S&P 500 symbols.");
        TraceLog.InfoState("YFinance.Exerciser", "TickerResolutionComplete", [new("ticker_count", tickers.Count), new("cache_path", cachePath)]);
        await WarmHistoryAsync(client, tickers, runnerOptions, CancellationToken.None);

        for (int cycle = 1; cycle <= runnerOptions.TotalCycles; cycle++)
        {
            Console.WriteLine();
            Console.WriteLine($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} Cycle {cycle}/{runnerOptions.TotalCycles} starting.");
            TraceLog.InfoState("YFinance.Exerciser", "CycleStart", [new("cycle", cycle), new("ticker_count", tickers.Count)]);
            IReadOnlyList<QuoteSnapshot> quotes = await FetchQuotesByBatchAsync(client, tickers, CancellationToken.None);
            PrintSummary(quotes, cycle);
            TraceLog.InfoState("YFinance.Exerciser", "CycleComplete", [new("cycle", cycle), new("quote_count", quotes.Count)]);

            if (cycle < runnerOptions.TotalCycles)
            {
                Console.WriteLine($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} Sleeping {runnerOptions.RefreshInterval} before next cycle.");
                await Task.Delay(runnerOptions.RefreshInterval, CancellationToken.None);
            }
        }
        TraceLog.InfoState("YFinance.Exerciser", "ExerciserComplete", [new("cycles", runnerOptions.TotalCycles), new("ticker_count", tickers.Count)]);
    }

    private static async Task<List<string>> ResolveTickersAsync(YFinanceClient client, string cachePath, RunnerOptions runnerOptions, CancellationToken cancellationToken)
    {
        if (runnerOptions.SymbolOverrides.Count > 0)
        {
            TraceLog.InfoState("YFinance.Exerciser", "TickerResolutionOverride", [new("symbols", runnerOptions.SymbolOverrides)]);
            return runnerOptions.SymbolOverrides.ToList();
        }

        return await GetTopTickersAsync(client, cachePath, runnerOptions, cancellationToken);
    }

    private static async Task<List<string>> GetTopTickersAsync(YFinanceClient client, string cachePath, RunnerOptions runnerOptions, CancellationToken cancellationToken)
    {
        CacheEnvelope? cache = await LoadCacheAsync(cachePath, cancellationToken);
        if (cache is not null && DateTimeOffset.UtcNow - cache.TimestampUtc < TimeSpan.FromMinutes(runnerOptions.CacheTtlMinutes))
        {
            TraceLog.InfoState("YFinance.Exerciser", "TickerResolutionCacheHit", [new("cache_path", cachePath), new("cached_count", cache.Items.Count)]);
            return cache.Items.OrderByDescending(static item => item.MarketCap)
                              .Take(runnerOptions.TopCount)
                              .Select(static item => item.Symbol)
                              .ToList();
        }
        TraceLog.InfoState("YFinance.Exerciser", "TickerResolutionCacheMiss", [new("cache_path", cachePath)]);

        List<string> symbols = await GetSp500SymbolsAsync(cancellationToken);
        List<CacheItem> items = new();
        foreach (string symbol in symbols)
        {
            try
            {
                TickerInfo? info = await client.Ticker(symbol).GetInfoAsync(cancellationToken);
                if (info?.MarketCap is > 0)
                {
                    items.Add(new CacheItem(symbol, info.MarketCap.Value));
                }
                Console.WriteLine($"{DateTimeOffset.Now:HH:mm:ss} MCAP {symbol,-8} {(info?.MarketCap?.ToString() ?? "n/a")}");
                TraceLog.InfoState("YFinance.Exerciser", "MarketCapProbe", [new("symbol", symbol), new("market_cap", info?.MarketCap), new("resolved", info is not null)]);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{DateTimeOffset.Now:HH:mm:ss} MCAP {symbol,-8} FAIL {ex.GetType().Name}: {ex.Message}");
                TraceLog.ErrorState("YFinance.Exerciser", "MarketCapProbeFailed", [new("symbol", symbol)], ex);
            }

            await Task.Delay(runnerOptions.PerTickerDelay, cancellationToken);
        }

        CacheEnvelope fresh = new(DateTimeOffset.UtcNow, items);
        await SaveCacheAsync(cachePath, fresh, cancellationToken);
        TraceLog.InfoState("YFinance.Exerciser", "TickerResolutionCacheStore", [new("cache_path", cachePath), new("resolved_count", items.Count)]);
        return items.OrderByDescending(static item => item.MarketCap)
                    .Take(runnerOptions.TopCount)
                    .Select(static item => item.Symbol)
                    .ToList();
    }

    private static async Task WarmHistoryAsync(YFinanceClient client, IReadOnlyList<string> symbols, RunnerOptions runnerOptions, CancellationToken cancellationToken)
    {
        int warmCount = Math.Min(runnerOptions.HistoryWarmupCount, symbols.Count);
        if (warmCount <= 0)
        {
            return;
        }

        DateTimeOffset endUtc = DateTimeOffset.UtcNow;
        DateTimeOffset startUtc = endUtc.AddDays(-runnerOptions.HistoryLookbackDays);
        Console.WriteLine($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} Warming {warmCount} history lanes over the last {runnerOptions.HistoryLookbackDays} days.");
        TraceLog.InfoState("YFinance.Exerciser", "HistoryWarmupStart", [new("warm_count", warmCount), new("lookback_days", runnerOptions.HistoryLookbackDays)]);
        for (int index = 0; index < warmCount; index++)
        {
            string symbol = symbols[index];
            try
            {
                HistoryResponse history = await client.Ticker(symbol).GetHistoryResponseAsync(startUtc, endUtc, "1d", cancellationToken).ConfigureAwait(false);
                Console.WriteLine($"{DateTimeOffset.Now:HH:mm:ss} HISTORY {symbol,-8} bars={history.Bars.Count} tz={history.Metadata?.ExchangeTimezoneName ?? "n/a"}");
                TraceLog.InfoState("YFinance.Exerciser", "HistoryWarmupItem", [new("symbol", symbol), new("bar_count", history.Bars.Count), new("timezone", history.Metadata?.ExchangeTimezoneName ?? "n/a")]);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{DateTimeOffset.Now:HH:mm:ss} HISTORY {symbol,-8} FAIL {ex.GetType().Name}: {ex.Message}");
                TraceLog.ErrorState("YFinance.Exerciser", "HistoryWarmupFailed", [new("symbol", symbol)], ex);
            }
        }
        TraceLog.InfoState("YFinance.Exerciser", "HistoryWarmupComplete", [new("warm_count", warmCount)]);
    }

    private static async Task<IReadOnlyList<QuoteSnapshot>> FetchQuotesByBatchAsync(YFinanceClient client, IReadOnlyList<string> symbols, CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, QuoteSnapshot> results = await client.Tickers(symbols).GetQuotesAsync(cancellationToken).ConfigureAwait(false);
        List<QuoteSnapshot> ordered = new(symbols.Count);
        foreach (string symbol in symbols)
        {
            if (results.TryGetValue(symbol, out QuoteSnapshot? quote))
            {
                ordered.Add(quote);
                Console.WriteLine($"{DateTimeOffset.Now:HH:mm:ss} QUOTE {symbol,-8} price={quote.RegularMarketPrice} change={quote.ComputedChangePercent:+0.00;-0.00;0.00}%");
                TraceLog.InfoState("YFinance.Exerciser", "QuoteObserved", [new("symbol", symbol), new("price", quote.RegularMarketPrice), new("change_percent", quote.ComputedChangePercent)]);
            }
            else
            {
                Console.WriteLine($"{DateTimeOffset.Now:HH:mm:ss} QUOTE {symbol,-8} missing");
                TraceLog.WarnState("YFinance.Exerciser", "QuoteMissing", [new("symbol", symbol)]);
            }
        }

        return ordered;
    }

    private static void PrintSummary(IReadOnlyList<QuoteSnapshot> quotes, int cycle)
    {
        long positives = quotes.LongCount(static quote => (quote.ComputedChangePercent ?? 0m) > 0m);
        long negatives = quotes.LongCount(static quote => (quote.ComputedChangePercent ?? 0m) < 0m);
        long unchanged = quotes.Count - positives - negatives;

        Console.WriteLine($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} Cycle {cycle} complete. Quotes={quotes.Count}, Up={positives}, Down={negatives}, Flat={unchanged}");
        foreach (QuoteSnapshot quote in quotes.Take(12))
        {
            Console.WriteLine($"{DateTimeOffset.Now:HH:mm:ss} SUMMARY {quote.Symbol,-8} price={quote.RegularMarketPrice} change={quote.ComputedChangePercent:+0.00;-0.00;0.00}% mcap={quote.MarketCap}");
        }
    }

    private static async Task<List<string>> GetSp500SymbolsAsync(CancellationToken cancellationToken)
    {
        using HttpClient httpClient = new();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
        string html = await httpClient.GetStringAsync(WikipediaUrl, cancellationToken);

        Match tableMatch = ConstituentsTableRegex().Match(html);
        if (tableMatch.Success)
        {
            MatchCollection rowMatches = ConstituentsRowRegex().Matches(tableMatch.Groups["body"].Value);
            List<string> symbols = new();
            foreach (Match rowMatch in rowMatches)
            {
                string symbol = WebUtility.HtmlDecode(rowMatch.Groups["symbol"].Value.Trim());
                if (string.IsNullOrWhiteSpace(symbol))
                {
                    continue;
                }

                string normalized = symbol.Replace('.', '-').ToUpperInvariant();
                if (!symbols.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                {
                    symbols.Add(normalized);
                }
            }

            if (symbols.Count > 0)
            {
                return symbols;
            }
        }

        return
        [
            "AAPL","MSFT","GOOGL","AMZN","NVDA","META","BRK-B","LLY","TSLA","V",
            "JPM","WMT","XOM","MA","NFLX","COST","UNH","JNJ","PG","HD"
        ];
    }

    private static async Task<CacheEnvelope?> LoadCacheAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        await using FileStream stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<CacheEnvelope>(stream, cancellationToken: cancellationToken);
    }

    private static async Task SaveCacheAsync(string path, CacheEnvelope cache, CancellationToken cancellationToken)
    {
        await using FileStream stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, cache, new JsonSerializerOptions { WriteIndented = true }, cancellationToken);
    }

    [GeneratedRegex("<table[^>]*id=\"constituents\"[^>]*>.*?<tbody>(?<body>.*?)</tbody>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ConstituentsTableRegex();

    [GeneratedRegex("<tr>\\s*<td[^>]*>(?:<a[^>]*>)?(?<symbol>[^<]+)", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ConstituentsRowRegex();

    private sealed record CacheEnvelope(DateTimeOffset TimestampUtc, List<CacheItem> Items);
    private sealed record CacheItem(string Symbol, long MarketCap);

    private sealed record RunnerOptions(
        int TopCount,
        int TotalCycles,
        TimeSpan RefreshInterval,
        TimeSpan PerTickerDelay,
        int CacheTtlMinutes,
        int HistoryWarmupCount,
        int HistoryLookbackDays,
        IReadOnlyList<string> SymbolOverrides)
    {
        public static RunnerOptions Parse(string[] args)
        {
            int topCount = DefaultTopCount;
            int totalCycles = DefaultTotalCycles;
            TimeSpan refreshInterval = DefaultRefreshInterval;
            TimeSpan perTickerDelay = DefaultPerTickerDelay;
            int cacheTtlMinutes = DefaultCacheTtlMinutes;
            int historyWarmupCount = DefaultHistoryWarmupCount;
            int historyLookbackDays = DefaultHistoryLookbackDays;
            List<string> symbolOverrides = new();

            for (int index = 0; index < args.Length; index++)
            {
                string arg = args[index];
                if (!arg.StartsWith("--", StringComparison.Ordinal))
                {
                    continue;
                }

                string? value = index + 1 < args.Length ? args[index + 1] : null;
                switch (arg)
                {
                    case "--top-count" when int.TryParse(value, out int parsedTopCount):
                        topCount = parsedTopCount;
                        index++;
                        break;
                    case "--cycles" when int.TryParse(value, out int parsedCycles):
                        totalCycles = parsedCycles;
                        index++;
                        break;
                    case "--refresh-seconds" when int.TryParse(value, out int refreshSeconds):
                        refreshInterval = TimeSpan.FromSeconds(refreshSeconds);
                        index++;
                        break;
                    case "--per-ticker-delay-ms" when int.TryParse(value, out int delayMs):
                        perTickerDelay = TimeSpan.FromMilliseconds(delayMs);
                        index++;
                        break;
                    case "--cache-ttl-minutes" when int.TryParse(value, out int parsedCacheTtlMinutes):
                        cacheTtlMinutes = parsedCacheTtlMinutes;
                        index++;
                        break;
                    case "--cache-ttl-hours" when int.TryParse(value, out int parsedCacheTtlHours):
                        cacheTtlMinutes = parsedCacheTtlHours * 60;
                        index++;
                        break;
                    case "--history-warmup-count" when int.TryParse(value, out int parsedWarmupCount):
                        historyWarmupCount = parsedWarmupCount;
                        index++;
                        break;
                    case "--history-lookback-days" when int.TryParse(value, out int parsedLookbackDays):
                        historyLookbackDays = parsedLookbackDays;
                        index++;
                        break;
                    case "--symbols" when !string.IsNullOrWhiteSpace(value):
                        symbolOverrides = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                              .Select(static symbol => symbol.ToUpperInvariant())
                                              .Distinct(StringComparer.OrdinalIgnoreCase)
                                              .ToList();
                        index++;
                        break;
                }
            }

            return new RunnerOptions(topCount, totalCycles, refreshInterval, perTickerDelay, cacheTtlMinutes, historyWarmupCount, historyLookbackDays, symbolOverrides);
        }
    }

    private sealed class PortfolioSaverTraceSink : IYFinanceTraceSink
    {
        public void InfoState(string source, string eventName, IEnumerable<KeyValuePair<string, object?>> fields)
            => TraceLog.InfoState(source, eventName, fields);

        public void WarnState(string source, string eventName, IEnumerable<KeyValuePair<string, object?>> fields)
            => TraceLog.WarnState(source, eventName, fields);

        public void ErrorState(string source, string eventName, IEnumerable<KeyValuePair<string, object?>> fields, Exception? exception = null)
            => TraceLog.ErrorState(source, eventName, fields, exception);
    }
}
