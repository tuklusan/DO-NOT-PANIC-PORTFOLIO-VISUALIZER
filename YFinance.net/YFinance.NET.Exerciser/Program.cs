using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using YFinance.NET.Api;
using YFinance.NET.Config;
using YFinance.NET.Models;

namespace YFinance.NET.Exerciser;

internal static partial class Program
{
    private const string WikipediaUrl = "https://en.wikipedia.org/wiki/List_of_S%26P_500_companies";
    private const string CacheFileName = "sp500-top100-cache.json";
    private const int DefaultCacheTtlHours = 6;
    private const int DefaultTotalCycles = 5;
    private const int DefaultTopCount = 100;
    private const int DefaultHistoryWarmupCount = 12;
    private const int DefaultHistoryLookbackDays = 5;
    private static readonly TimeSpan DefaultRefreshInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DefaultPerTickerDelay = TimeSpan.FromMilliseconds(500);

    private static async Task Main(string[] args)
    {
        RunnerOptions runnerOptions = RunnerOptions.Parse(args);
        YFinanceOptions options = new()
        {
            MinimumRequestSpacing = runnerOptions.PerTickerDelay,
            MaxRetries = 3,
            DefaultCacheTtl = TimeSpan.FromMinutes(30),
            SummaryCacheTtl = TimeSpan.FromHours(6),
            PersistentMetadataCacheTtl = TimeSpan.FromHours(Math.Max(1, runnerOptions.CacheTtlHours)),
            MaxSymbolsPerQuoteRequest = 25
        };

        using YFinanceClient client = new(options);
        string cachePath = Path.Combine(AppContext.BaseDirectory, CacheFileName);
        List<string> tickers = await ResolveTickersAsync(client, cachePath, runnerOptions, CancellationToken.None);
        Console.WriteLine($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} Loaded top {tickers.Count} S&P 500 symbols.");
        await WarmHistoryAsync(client, tickers, runnerOptions, CancellationToken.None);

        for (int cycle = 1; cycle <= runnerOptions.TotalCycles; cycle++)
        {
            Console.WriteLine();
            Console.WriteLine($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} Cycle {cycle}/{runnerOptions.TotalCycles} starting.");
            IReadOnlyList<QuoteSnapshot> quotes = await FetchQuotesByBatchAsync(client, tickers, CancellationToken.None);
            PrintSummary(quotes, cycle);

            if (cycle < runnerOptions.TotalCycles)
            {
                Console.WriteLine($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} Sleeping {runnerOptions.RefreshInterval} before next cycle.");
                await Task.Delay(runnerOptions.RefreshInterval, CancellationToken.None);
            }
        }
    }

    private static async Task<List<string>> ResolveTickersAsync(YFinanceClient client, string cachePath, RunnerOptions runnerOptions, CancellationToken cancellationToken)
    {
        if (runnerOptions.SymbolOverrides.Count > 0)
        {
            return runnerOptions.SymbolOverrides.ToList();
        }

        return await GetTopTickersAsync(client, cachePath, runnerOptions, cancellationToken);
    }

    private static async Task<List<string>> GetTopTickersAsync(YFinanceClient client, string cachePath, RunnerOptions runnerOptions, CancellationToken cancellationToken)
    {
        CacheEnvelope? cache = await LoadCacheAsync(cachePath, cancellationToken);
        if (cache is not null && DateTimeOffset.UtcNow - cache.TimestampUtc < TimeSpan.FromHours(runnerOptions.CacheTtlHours))
        {
            return cache.Items.OrderByDescending(static item => item.MarketCap)
                              .Take(runnerOptions.TopCount)
                              .Select(static item => item.Symbol)
                              .ToList();
        }

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
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{DateTimeOffset.Now:HH:mm:ss} MCAP {symbol,-8} FAIL {ex.GetType().Name}: {ex.Message}");
            }

            await Task.Delay(runnerOptions.PerTickerDelay, cancellationToken);
        }

        CacheEnvelope fresh = new(DateTimeOffset.UtcNow, items);
        await SaveCacheAsync(cachePath, fresh, cancellationToken);
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
        for (int index = 0; index < warmCount; index++)
        {
            string symbol = symbols[index];
            try
            {
                HistoryResponse history = await client.Ticker(symbol).GetHistoryResponseAsync(startUtc, endUtc, "1d", cancellationToken).ConfigureAwait(false);
                Console.WriteLine($"{DateTimeOffset.Now:HH:mm:ss} HISTORY {symbol,-8} bars={history.Bars.Count} tz={history.Metadata?.ExchangeTimezoneName ?? "n/a"}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{DateTimeOffset.Now:HH:mm:ss} HISTORY {symbol,-8} FAIL {ex.GetType().Name}: {ex.Message}");
            }
        }
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
            }
            else
            {
                Console.WriteLine($"{DateTimeOffset.Now:HH:mm:ss} QUOTE {symbol,-8} missing");
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
        int CacheTtlHours,
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
            int cacheTtlHours = DefaultCacheTtlHours;
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
                    case "--cache-ttl-hours" when int.TryParse(value, out int parsedCacheTtlHours):
                        cacheTtlHours = parsedCacheTtlHours;
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
                                              .Select(static symbol => symbol.Replace('.', '-').ToUpperInvariant())
                                              .Distinct(StringComparer.OrdinalIgnoreCase)
                                              .ToList();
                        index++;
                        break;
                }
            }

            return new RunnerOptions(topCount, totalCycles, refreshInterval, perTickerDelay, cacheTtlHours, historyWarmupCount, historyLookbackDays, symbolOverrides);
        }
    }
}
