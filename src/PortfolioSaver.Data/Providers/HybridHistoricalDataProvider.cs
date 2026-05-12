using System.Globalization;
using System.Text.Json;
using PortfolioSaver.Core.Enums;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Core.Services;
using PortfolioSaver.Data.Interfaces;
using PortfolioSaver.Data.Services;

namespace PortfolioSaver.Data.Providers;

public sealed class HybridHistoricalDataProvider : IHistoricalDataProvider
{
    private const int MaxYahooSparkBatchSymbols = 24;
    private readonly IHistoricalCacheService _cacheService;
    private readonly HttpClient _httpClient;
    private readonly string _finnhubApiKey;
    private readonly string _twelveDataApiKey;
    private readonly TimeSpan _cacheFreshness;
    private readonly RateLimitGuard _finnhubGuard = new();
    private readonly RateLimitGuard _twelveDataGuard = new();
    private readonly RetryPolicyService _retryPolicy = new();
    private readonly YahooFinanceSessionService _yahooSessionService;
    private readonly TimeSpan _minFinnhubSpacing;
    private readonly TimeSpan _minTwelveDataSpacing;
    private readonly int _rotationSeed;
    private readonly IReadOnlyDictionary<string, SymbolProfile> _symbolProfiles;

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
        _httpClient = httpClient ?? new HttpClient();
        _finnhubApiKey = finnhubApiKey ?? string.Empty;
        _twelveDataApiKey = twelveDataApiKey ?? string.Empty;
        _cacheFreshness = cacheFreshness ?? TimeSpan.FromHours(12);
        _minFinnhubSpacing = minFinnhubSpacing ?? TimeSpan.FromSeconds(2);
        _minTwelveDataSpacing = minTwelveDataSpacing ?? TimeSpan.FromSeconds(15);
        _rotationSeed = rotationSeed;
        _symbolProfiles = symbolProfiles ?? new Dictionary<string, SymbolProfile>(StringComparer.OrdinalIgnoreCase);
        _yahooSessionService = new YahooFinanceSessionService(_httpClient);
    }

    public async Task<IReadOnlyList<TickerHistorySnapshot>> GetHistoryAsync(
        IEnumerable<string> symbols,
        int lookbackDays,
        CancellationToken cancellationToken = default)
    {
        List<string> orderedSymbols = symbols
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (orderedSymbols.Count == 0)
            return [];

        await _cacheService.PurgeExpiredAsync(cancellationToken);

        Dictionary<string, TickerHistorySnapshot> freshOrFetched = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, TickerHistorySnapshot> staleCache = new(StringComparer.OrdinalIgnoreCase);
        List<string> pendingSymbols = [];

        foreach (string symbol in orderedSymbols)
        {
            TickerHistorySnapshot? cached = await _cacheService.LoadAsync(symbol, cancellationToken);
            if (cached is not null && cached.LookbackDays == lookbackDays && cached.IsFresh(_cacheFreshness))
            {
                freshOrFetched[symbol] = cached;
                continue;
            }

            if (cached is not null)
                staleCache[symbol] = cached;

            pendingSymbols.Add(symbol);
        }

        if (pendingSymbols.Count > 0)
        {
            HashSet<string> resolvedSymbols = new(StringComparer.OrdinalIgnoreCase);
            List<string> yahooBatchSymbols = pendingSymbols
                .Where(symbol => DataSourceSymbolEligibility.IsHistoryEligible(DataSourceKind.YahooFinance, symbol, _symbolProfiles))
                .ToList();

            IReadOnlyDictionary<string, TickerHistorySnapshot> yahooSparkSnapshots =
                await TryFetchFromYahooSparkBatchAsync(yahooBatchSymbols, lookbackDays, cancellationToken);
            foreach ((string symbol, TickerHistorySnapshot snapshot) in yahooSparkSnapshots)
            {
                if (snapshot.Points.Count == 0)
                    continue;

                freshOrFetched[symbol] = snapshot;
                resolvedSymbols.Add(symbol);
                await _cacheService.SaveAsync(snapshot, cancellationToken);
            }

            foreach (string symbol in pendingSymbols)
            {
                if (resolvedSymbols.Contains(symbol))
                    continue;

                TickerHistorySnapshot? fetched = await TryFetchHistoryAsync(symbol, lookbackDays, cancellationToken);
                if (fetched is not null && fetched.Points.Count > 0)
                {
                    freshOrFetched[symbol] = fetched;
                    await _cacheService.SaveAsync(fetched, cancellationToken);
                }
            }
        }

        List<TickerHistorySnapshot> results = [];
        foreach (string symbol in orderedSymbols)
        {
            if (freshOrFetched.TryGetValue(symbol, out TickerHistorySnapshot? fetched))
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

        return results;
    }

    private async Task<TickerHistorySnapshot?> TryFetchHistoryAsync(string symbol, int lookbackDays, CancellationToken cancellationToken)
    {
        Exception? lastException = null;

        foreach ((string ProviderLabel, Func<Task<TickerHistorySnapshot>> FetchAsync) fetchPlan in GetFetchPlans(symbol, lookbackDays, cancellationToken))
        {
            try
            {
                return await _retryPolicy.ExecuteAsync(fetchPlan.FetchAsync, cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                lastException = ex;
            }
        }

        if (lastException is not null)
            System.Diagnostics.Debug.WriteLine(lastException);

        return null;
    }

    private IReadOnlyList<(string ProviderLabel, Func<Task<TickerHistorySnapshot>> FetchAsync)> GetFetchPlans(
        string symbol,
        int lookbackDays,
        CancellationToken cancellationToken)
    {
        List<(string ProviderLabel, Func<Task<TickerHistorySnapshot>> FetchAsync)> plans = [];

        if (DataSourceSymbolEligibility.IsHistoryEligible(DataSourceKind.YahooFinance, symbol, _symbolProfiles))
            plans.Add(("Yahoo Finance", () => FetchFromYahooFinanceAsync(symbol, lookbackDays, cancellationToken)));

        List<(string ProviderLabel, Func<Task<TickerHistorySnapshot>> FetchAsync)> backups = [];
        if (!string.IsNullOrWhiteSpace(_finnhubApiKey) &&
            DataSourceSymbolEligibility.IsHistoryEligible(DataSourceKind.Finnhub, symbol, _symbolProfiles))
        {
            backups.Add(("Finnhub", () => FetchFromFinnhubAsync(symbol, lookbackDays, cancellationToken)));
        }

        if (!string.IsNullOrWhiteSpace(_twelveDataApiKey) &&
            DataSourceSymbolEligibility.IsHistoryEligible(DataSourceKind.TwelveData, symbol, _symbolProfiles))
        {
            backups.Add(("Twelve Data", () => FetchFromTwelveDataAsync(symbol, lookbackDays, cancellationToken)));
        }

        if (backups.Count > 1)
        {
            int normalizedSeed = Math.Abs(StringComparer.OrdinalIgnoreCase.GetHashCode(symbol) + _rotationSeed) % backups.Count;
            if (normalizedSeed > 0)
                backups = RotatePlans(backups, normalizedSeed);
        }

        plans.AddRange(backups);
        return plans;
    }

    private static List<(string ProviderLabel, Func<Task<TickerHistorySnapshot>> FetchAsync)> RotatePlans(
        IReadOnlyList<(string ProviderLabel, Func<Task<TickerHistorySnapshot>> FetchAsync)> plans,
        int rotation)
    {
        List<(string ProviderLabel, Func<Task<TickerHistorySnapshot>> FetchAsync)> rotated = [];
        for (int i = 0; i < plans.Count; i++)
            rotated.Add(plans[(rotation + i) % plans.Count]);

        return rotated;
    }

    private async Task<IReadOnlyDictionary<string, TickerHistorySnapshot>> TryFetchFromYahooSparkBatchAsync(
        IReadOnlyList<string> symbols,
        int lookbackDays,
        CancellationToken cancellationToken)
    {
        Dictionary<string, TickerHistorySnapshot> snapshots = new(StringComparer.OrdinalIgnoreCase);
        if (symbols.Count == 0)
            return snapshots;

        foreach (List<string> batch in ChunkSymbols(symbols, MaxYahooSparkBatchSymbols))
        {
            try
            {
                string symbolsCsv = string.Join(",", batch.Select(Uri.EscapeDataString));
                (string interval, int rangeDays) = GetYahooRequestShape(lookbackDays);
                string range = interval == "1h"
                    ? $"{Math.Max(2, rangeDays)}d"
                    : $"{Math.Max(7, rangeDays)}d";
                string url = $"https://query1.finance.yahoo.com/v8/finance/spark?symbols={symbolsCsv}&range={range}&interval={interval}&includePrePost=false";
                using HttpResponseMessage response = await _yahooSessionService.GetAsync(url, cancellationToken);
                response.EnsureSuccessStatusCode();

                await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                if (!document.RootElement.TryGetProperty("spark", out JsonElement sparkElement) ||
                    !sparkElement.TryGetProperty("result", out JsonElement resultArray) ||
                    resultArray.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (JsonElement resultElement in resultArray.EnumerateArray())
                {
                    string? symbol = resultElement.TryGetProperty("symbol", out JsonElement symbolElement) && symbolElement.ValueKind == JsonValueKind.String
                        ? symbolElement.GetString()
                        : null;
                    if (string.IsNullOrWhiteSpace(symbol))
                        continue;

                    if (!TryBuildSparkHistorySnapshot(symbol, lookbackDays, resultElement, out TickerHistorySnapshot? snapshot) || snapshot is null)
                        continue;

                    snapshots[symbol] = snapshot;
                }
            }
            catch
            {
                // Keep pipeline resilient; unresolved symbols will continue to per-symbol fallback.
            }
        }

        return snapshots;
    }

    private async Task<TickerHistorySnapshot> FetchFromFinnhubAsync(string symbol, int lookbackDays, CancellationToken cancellationToken)
    {
        await _finnhubGuard.WaitIfNeededAsync(_minFinnhubSpacing, cancellationToken);

        DateTimeOffset end = DateTimeOffset.UtcNow;
        DateTimeOffset start = end.AddDays(-lookbackDays - 2);
        string url = $"https://finnhub.io/api/v1/stock/candle?symbol={Uri.EscapeDataString(symbol)}&resolution=D&from={start.ToUnixTimeSeconds()}&to={end.ToUnixTimeSeconds()}&token={Uri.EscapeDataString(_finnhubApiKey)}";

        using HttpResponseMessage response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        string status = document.RootElement.TryGetProperty("s", out JsonElement statusElement)
            ? statusElement.GetString() ?? string.Empty
            : string.Empty;

        if (!string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Finnhub returned '{status}' for {symbol}.");

        List<HistoricalPricePoint> points = [];
        JsonElement closes = document.RootElement.GetProperty("c");
        JsonElement times = document.RootElement.GetProperty("t");
        int pointCount = Math.Min(closes.GetArrayLength(), times.GetArrayLength());
        for (int i = 0; i < pointCount; i++)
        {
            if (!closes[i].TryGetDecimal(out decimal close))
                continue;

            long unixTime = times[i].GetInt64();
            points.Add(new HistoricalPricePoint
            {
                TimestampUtc = DateTimeOffset.FromUnixTimeSeconds(unixTime),
                Close = close
            });
        }

        return BuildSnapshot(symbol, lookbackDays, points);
    }

    private async Task<TickerHistorySnapshot> FetchFromTwelveDataAsync(string symbol, int lookbackDays, CancellationToken cancellationToken)
    {
        await _twelveDataGuard.WaitIfNeededAsync(_minTwelveDataSpacing, cancellationToken);

        int outputSize = Math.Max(lookbackDays + 6, 20);
        string url = $"https://api.twelvedata.com/time_series?symbol={Uri.EscapeDataString(symbol)}&interval=1day&outputsize={outputSize}&order=ASC&apikey={Uri.EscapeDataString(_twelveDataApiKey)}";

        using HttpResponseMessage response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (document.RootElement.TryGetProperty("code", out _))
            throw new InvalidOperationException(document.RootElement.GetProperty("message").GetString() ?? $"Twelve Data history failed for {symbol}.");

        if (!document.RootElement.TryGetProperty("values", out JsonElement valuesElement) || valuesElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"Twelve Data returned no history points for {symbol}.");

        List<HistoricalPricePoint> points = [];
        foreach (JsonElement pointElement in valuesElement.EnumerateArray())
        {
            string? closeText = pointElement.TryGetProperty("close", out JsonElement closeElement) ? closeElement.GetString() : null;
            string? timestampText = pointElement.TryGetProperty("datetime", out JsonElement datetimeElement) ? datetimeElement.GetString() : null;

            if (!decimal.TryParse(closeText, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal close))
                continue;

            if (!TryParseTimestamp(timestampText, out DateTimeOffset timestamp))
                continue;

            points.Add(new HistoricalPricePoint
            {
                TimestampUtc = timestamp,
                Close = close
            });
        }

        return BuildSnapshot(symbol, lookbackDays, points);
    }

    private async Task<TickerHistorySnapshot> FetchFromYahooFinanceAsync(string symbol, int lookbackDays, CancellationToken cancellationToken)
    {
        DateTimeOffset end = DateTimeOffset.UtcNow;
        (string interval, int rangeDays) = GetYahooRequestShape(lookbackDays);
        DateTimeOffset start = end.AddDays(-Math.Max(rangeDays, lookbackDays + 1));
        string url = $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}?interval={interval}&includePrePost=false&period1={start.ToUnixTimeSeconds()}&period2={end.ToUnixTimeSeconds()}";

        using HttpResponseMessage response = await _yahooSessionService.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("chart", out JsonElement chartElement) ||
            !chartElement.TryGetProperty("result", out JsonElement resultElement) ||
            resultElement.ValueKind != JsonValueKind.Array ||
            resultElement.GetArrayLength() == 0)
        {
            throw new InvalidOperationException($"Yahoo Finance returned no chart data for {symbol}.");
        }

        JsonElement chartResult = resultElement[0];
        if (!chartResult.TryGetProperty("timestamp", out JsonElement timestampElement) || timestampElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"Yahoo Finance returned no timestamps for {symbol}.");

        if (!chartResult.TryGetProperty("indicators", out JsonElement indicatorsElement) ||
            !indicatorsElement.TryGetProperty("quote", out JsonElement quoteArray) ||
            quoteArray.ValueKind != JsonValueKind.Array ||
            quoteArray.GetArrayLength() == 0)
        {
            throw new InvalidOperationException($"Yahoo Finance returned no indicators for {symbol}.");
        }

        JsonElement quoteElement = quoteArray[0];
        if (!quoteElement.TryGetProperty("close", out JsonElement closesElement) || closesElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"Yahoo Finance returned no closes for {symbol}.");

        int pointCount = Math.Min(timestampElement.GetArrayLength(), closesElement.GetArrayLength());
        List<HistoricalPricePoint> points = [];
        for (int i = 0; i < pointCount; i++)
        {
            JsonElement closeElement = closesElement[i];
            if (closeElement.ValueKind != JsonValueKind.Number || !closeElement.TryGetDecimal(out decimal close))
                continue;

            JsonElement timeElement = timestampElement[i];
            if (timeElement.ValueKind != JsonValueKind.Number || !timeElement.TryGetInt64(out long unixTime))
                continue;

            points.Add(new HistoricalPricePoint
            {
                TimestampUtc = DateTimeOffset.FromUnixTimeSeconds(unixTime),
                Close = close
            });
        }

        return BuildSnapshot(symbol, lookbackDays, points);
    }

    private static bool TryBuildSparkHistorySnapshot(
        string symbol,
        int lookbackDays,
        JsonElement resultElement,
        out TickerHistorySnapshot? snapshot)
    {
        snapshot = null;
        if (!resultElement.TryGetProperty("response", out JsonElement responseArray) ||
            responseArray.ValueKind != JsonValueKind.Array ||
            responseArray.GetArrayLength() == 0)
        {
            return false;
        }

        JsonElement responseElement = responseArray[0];
        List<HistoricalPricePoint> points = ExtractSparkPoints(responseElement);
        if (points.Count == 0)
            return false;

        snapshot = BuildSnapshot(symbol, lookbackDays, points);
        return snapshot.Points.Count > 0;
    }

    private static (string Interval, int RangeDays) GetYahooRequestShape(int lookbackDays)
        => lookbackDays <= 1
            ? ("1h", 2)
            : ("1d", Math.Max(7, lookbackDays));

    private static List<HistoricalPricePoint> ExtractSparkPoints(JsonElement responseElement)
    {
        if (!responseElement.TryGetProperty("timestamp", out JsonElement timestampElement) || timestampElement.ValueKind != JsonValueKind.Array)
            return [];

        if (!responseElement.TryGetProperty("indicators", out JsonElement indicatorsElement) ||
            !indicatorsElement.TryGetProperty("quote", out JsonElement quoteArray) ||
            quoteArray.ValueKind != JsonValueKind.Array ||
            quoteArray.GetArrayLength() == 0)
        {
            return [];
        }

        JsonElement quoteElement = quoteArray[0];
        if (!quoteElement.TryGetProperty("close", out JsonElement closesElement) || closesElement.ValueKind != JsonValueKind.Array)
            return [];

        int pointCount = Math.Min(timestampElement.GetArrayLength(), closesElement.GetArrayLength());
        List<HistoricalPricePoint> points = [];
        for (int i = 0; i < pointCount; i++)
        {
            JsonElement closeElement = closesElement[i];
            if (closeElement.ValueKind != JsonValueKind.Number || !closeElement.TryGetDecimal(out decimal close))
                continue;

            JsonElement timeElement = timestampElement[i];
            if (timeElement.ValueKind != JsonValueKind.Number || !timeElement.TryGetInt64(out long unixTime))
                continue;

            points.Add(new HistoricalPricePoint
            {
                TimestampUtc = DateTimeOffset.FromUnixTimeSeconds(unixTime),
                Close = close
            });
        }

        return points;
    }

    private static IEnumerable<List<string>> ChunkSymbols(IReadOnlyList<string> symbols, int maxChunkSize)
    {
        if (symbols.Count == 0 || maxChunkSize <= 0)
            yield break;

        for (int index = 0; index < symbols.Count; index += maxChunkSize)
        {
            int count = Math.Min(maxChunkSize, symbols.Count - index);
            yield return symbols.Skip(index).Take(count).ToList();
        }
    }

    private static bool TryParseTimestamp(string? timestampText, out DateTimeOffset timestamp)
    {
        if (DateTimeOffset.TryParse(timestampText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out timestamp))
            return true;

        if (DateTime.TryParse(timestampText, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedDateTime))
        {
            timestamp = new DateTimeOffset(DateTime.SpecifyKind(parsedDateTime, DateTimeKind.Utc));
            return true;
        }

        timestamp = default;
        return false;
    }

    private static TickerHistorySnapshot BuildSnapshot(string symbol, int lookbackDays, List<HistoricalPricePoint> points)
    {
        List<HistoricalPricePoint> ordered = points
            .OrderBy(point => point.TimestampUtc)
            .ToList();

        DateTimeOffset cutoff = DateTimeOffset.UtcNow.AddDays(-lookbackDays);
        List<HistoricalPricePoint> filtered = ordered
            .Where(point => point.TimestampUtc >= cutoff)
            .ToList();

        if (lookbackDays <= 1 && filtered.Count < 2 && ordered.Count >= 2)
        {
            DateTimeOffset relaxedCutoff = ordered[^1].TimestampUtc.AddDays(-2);
            filtered = ordered
                .Where(point => point.TimestampUtc >= relaxedCutoff)
                .ToList();

            if (filtered.Count < 2)
                filtered = ordered.TakeLast(Math.Min(8, ordered.Count)).ToList();
        }

        return new TickerHistorySnapshot
        {
            Symbol = symbol,
            LookbackDays = lookbackDays,
            FetchTimestampUtc = DateTimeOffset.UtcNow,
            Points = filtered
        };
    }
}
