using System.Globalization;
using System.Net;
using System.Text.Json;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Data.Interfaces;
using PortfolioSaver.Data.Services;
using PortfolioSaver.Shared.Diagnostics;

namespace PortfolioSaver.Data.Providers;

public sealed class YahooFinanceQuoteProvider : IQuoteProvider
{
    private const int MaxSparkBatchSymbols = 24;
    private const int MaxQuoteEndpointBatchSymbols = 8;
    private readonly YahooFinanceSessionService _sessionService;

    public YahooFinanceQuoteProvider(HttpClient httpClient, YahooFinanceSessionService? sessionService = null)
    {
        _sessionService = sessionService ?? new YahooFinanceSessionService(httpClient);
    }

    public async Task<IReadOnlyList<QuoteSnapshot>> GetQuotesAsync(IEnumerable<string> symbols, CancellationToken cancellationToken = default)
    {
        List<string> requestedSymbols = symbols
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (requestedSymbols.Count == 0)
            return [];

        TraceLog.Info("YahooFinanceQuoteProvider", $"GetQuotesAsync requested={requestedSymbols.Count}");
        Dictionary<string, QuoteSnapshot> snapshots = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> unresolvedSymbols = requestedSymbols.ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool sawRateLimit = false;
        List<string> quoteEndpointFirstSymbols = requestedSymbols
            .Where(ShouldPreferQuoteEndpointLookup)
            .ToList();
        List<string> sparkSymbols = requestedSymbols
            .Where(symbol => !quoteEndpointFirstSymbols.Contains(symbol, StringComparer.OrdinalIgnoreCase))
            .ToList();

        foreach (List<string> batch in ChunkSymbols(quoteEndpointFirstSymbols, MaxQuoteEndpointBatchSymbols))
        {
            if (batch.Count == 0)
                continue;

            try
            {
                IReadOnlyDictionary<string, QuoteSnapshot> quoteEndpointSnapshots = await FetchQuoteEndpointBatchAsync(batch, cancellationToken);
                if (quoteEndpointSnapshots.Count > 0)
                {
                    TraceLog.Info("YahooFinanceQuoteProvider", $"Quote endpoint preferred batch requested={batch.Count}, resolved={quoteEndpointSnapshots.Count}");
                    foreach ((string symbol, QuoteSnapshot snapshot) in quoteEndpointSnapshots)
                    {
                        snapshots[symbol] = snapshot;
                        unresolvedSymbols.Remove(symbol);
                    }
                }
            }
            catch (Exception ex) when (IsTooManyRequests(ex))
            {
                sawRateLimit = true;
                TraceLog.Warn("YahooFinanceQuoteProvider", $"Quote endpoint preferred batch rate-limited for [{string.Join(", ", batch)}]: {ex.GetType().Name}: {ex.Message}");
                break;
            }
        }

        if (sawRateLimit)
            throw CreateRateLimitException("Yahoo Finance rate limited (429) during quote retrieval.", requestedSymbols, snapshots);

        foreach (List<string> batch in ChunkSymbols(sparkSymbols, MaxSparkBatchSymbols))
        {
            if (batch.Count == 0)
                continue;

            try
            {
                IReadOnlyDictionary<string, QuoteSnapshot> sparkSnapshots = await FetchSparkQuotesBatchAsync(batch, cancellationToken);
                TraceLog.Info("YahooFinanceQuoteProvider", $"Spark batch requested={batch.Count}, resolved={sparkSnapshots.Count}");
                foreach (string symbol in batch)
                {
                    if (!sparkSnapshots.TryGetValue(symbol, out QuoteSnapshot? snapshot))
                        continue;

                    snapshots[symbol] = snapshot;
                    unresolvedSymbols.Remove(symbol);
                }
            }
            catch (Exception ex)
            {
                if (IsTooManyRequests(ex))
                    sawRateLimit = true;
                TraceLog.Warn("YahooFinanceQuoteProvider", $"Spark batch failed for [{string.Join(", ", batch)}]: {ex.GetType().Name}: {ex.Message}");
                // Fall back to chart only if this was not an explicit rate limit.
                if (sawRateLimit)
                    break;
            }
        }

        if (sawRateLimit)
            throw CreateRateLimitException("Yahoo Finance rate limited (429) during spark retrieval.", requestedSymbols, snapshots);

        foreach (string symbol in quoteEndpointFirstSymbols.Concat(requestedSymbols))
        {
            if (!unresolvedSymbols.Contains(symbol))
                continue;

            QuoteSnapshot? chartSnapshot;
            try
            {
                chartSnapshot = await FetchChartQuoteAsync(symbol, cancellationToken);
            }
            catch (Exception ex)
            {
                if (IsTooManyRequests(ex))
                {
                    sawRateLimit = true;
                    TraceLog.Warn("YahooFinanceQuoteProvider", $"Chart quote rate-limited for {symbol}: {ex.GetType().Name}: {ex.Message}");
                    break;
                }

                TraceLog.Warn("YahooFinanceQuoteProvider", $"Chart quote failed for {symbol}: {ex.GetType().Name}: {ex.Message}");
                chartSnapshot = null;
            }

            if (chartSnapshot is null)
                continue;

            snapshots[symbol] = chartSnapshot;
            unresolvedSymbols.Remove(symbol);
        }

        bool hasUnresolvedDirectSymbols = quoteEndpointFirstSymbols.Any(unresolvedSymbols.Contains);
        bool shouldTryQuoteEndpointFallback = snapshots.Count == 0 || hasUnresolvedDirectSymbols;
        if (shouldTryQuoteEndpointFallback)
        {
            try
            {
                List<string> quoteFallbackSymbols = unresolvedSymbols.Count > 0
                    ? requestedSymbols.Where(unresolvedSymbols.Contains).ToList()
                    : requestedSymbols.ToList();

                IReadOnlyDictionary<string, QuoteSnapshot> quoteEndpointSnapshots = await FetchQuoteEndpointBatchAsync(quoteFallbackSymbols, cancellationToken);
                if (quoteEndpointSnapshots.Count > 0)
                {
                    TraceLog.Info("YahooFinanceQuoteProvider", $"Quote endpoint fallback resolved={quoteEndpointSnapshots.Count}");
                    foreach ((string symbol, QuoteSnapshot snapshot) in quoteEndpointSnapshots)
                    {
                        snapshots[symbol] = snapshot;
                        unresolvedSymbols.Remove(symbol);
                    }
                }
            }
            catch (Exception ex) when (IsTooManyRequests(ex))
            {
                throw CreateRateLimitException("Yahoo Finance rate limited (429) during quote retrieval.", requestedSymbols, snapshots);
            }
        }

        if (sawRateLimit && unresolvedSymbols.Count > 0)
            throw CreateRateLimitException("Yahoo Finance rate limited (429) during chart retrieval.", requestedSymbols, snapshots);

        if (snapshots.Count == 0)
        {
            TraceLog.Warn("YahooFinanceQuoteProvider", "No matching quotes returned from spark/chart/quote endpoint.");
            throw new InvalidOperationException("Yahoo Finance returned no matching quotes.");
        }

        return BuildOrderedResults(requestedSymbols, snapshots);
    }

    private async Task<IReadOnlyDictionary<string, QuoteSnapshot>> FetchQuoteEndpointBatchAsync(
        IReadOnlyList<string> symbols,
        CancellationToken cancellationToken)
    {
        if (symbols.Count == 0)
            return new Dictionary<string, QuoteSnapshot>(StringComparer.OrdinalIgnoreCase);

        string symbolsCsv = string.Join(",", symbols.Select(Uri.EscapeDataString));
        string url = $"https://query1.finance.yahoo.com/v7/finance/quote?symbols={symbolsCsv}";

        try
        {
            using HttpResponseMessage response = await _sessionService.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (!document.RootElement.TryGetProperty("quoteResponse", out JsonElement quoteResponse) ||
                !quoteResponse.TryGetProperty("result", out JsonElement resultArray) ||
                resultArray.ValueKind != JsonValueKind.Array)
            {
                return new Dictionary<string, QuoteSnapshot>(StringComparer.OrdinalIgnoreCase);
            }

            Dictionary<string, QuoteSnapshot> snapshots = new(StringComparer.OrdinalIgnoreCase);
            foreach (JsonElement item in resultArray.EnumerateArray())
            {
                string symbol = GetString(item, "symbol");
                if (string.IsNullOrWhiteSpace(symbol))
                    continue;

                decimal? last =
                    TryGetDecimal(item, "regularMarketPrice") ??
                    TryGetDecimal(item, "postMarketPrice") ??
                    TryGetDecimal(item, "preMarketPrice");
                decimal? previousClose =
                    TryGetDecimal(item, "regularMarketPreviousClose") ??
                    TryGetDecimal(item, "previousClose");
                decimal? change =
                    TryGetDecimal(item, "regularMarketChange") ??
                    (last is decimal l && previousClose is decimal p ? Math.Round(l - p, 4) : null);
                decimal? changePercent = TryGetDecimal(item, "regularMarketChangePercent");

                if (last is null && previousClose is null)
                    continue;

                DateTimeOffset? providerTimestampUtc = TryGetUnixTimestamp(item, "regularMarketTime");
                snapshots[symbol] = new QuoteSnapshot
                {
                    Symbol = symbol,
                    Last = last,
                    Change = change,
                    ChangePercent = changePercent,
                    PreviousClose = previousClose,
                    Currency = GetString(item, "currency"),
                    ProviderTimestampUtc = providerTimestampUtc,
                    FetchTimestampUtc = DateTimeOffset.UtcNow,
                    IsStale = false
                };
            }

            return snapshots;
        }
        catch (Exception ex)
        {
            if (IsTooManyRequests(ex))
                throw;

            TraceLog.Warn("YahooFinanceQuoteProvider", $"Quote endpoint fallback failed: {ex.GetType().Name}: {ex.Message}");
            return new Dictionary<string, QuoteSnapshot>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            IReadOnlyList<QuoteSnapshot> quotes = await GetQuotesAsync(["AAPL"], cancellationToken);
            return quotes.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task<IReadOnlyDictionary<string, QuoteSnapshot>> FetchSparkQuotesBatchAsync(
        IReadOnlyList<string> symbols,
        CancellationToken cancellationToken)
    {
        if (symbols.Count == 0)
            return new Dictionary<string, QuoteSnapshot>(StringComparer.OrdinalIgnoreCase);

        string symbolsCsv = string.Join(",", symbols.Select(Uri.EscapeDataString));
        string url = $"https://query1.finance.yahoo.com/v8/finance/spark?symbols={symbolsCsv}&range=7d&interval=1d&includePrePost=false";

        using HttpResponseMessage response = await _sessionService.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("spark", out JsonElement sparkElement) ||
            !sparkElement.TryGetProperty("result", out JsonElement resultArray) ||
            resultArray.ValueKind != JsonValueKind.Array)
        {
            return new Dictionary<string, QuoteSnapshot>(StringComparer.OrdinalIgnoreCase);
        }

        Dictionary<string, QuoteSnapshot> snapshots = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement resultElement in resultArray.EnumerateArray())
        {
            string symbol = GetString(resultElement, "symbol");
            if (string.IsNullOrWhiteSpace(symbol))
                continue;

            if (!TryBuildSparkQuoteSnapshot(symbol, resultElement, out QuoteSnapshot? snapshot) || snapshot is null)
                continue;

            snapshots[symbol] = snapshot;
        }

        return snapshots;
    }

    private async Task<QuoteSnapshot?> FetchChartQuoteAsync(string symbol, CancellationToken cancellationToken)
    {
        string url = $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}?interval=1d&range=7d&includePrePost=false";
        using HttpResponseMessage response = await _sessionService.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("chart", out JsonElement chartElement) ||
            !chartElement.TryGetProperty("result", out JsonElement resultElement) ||
            resultElement.ValueKind != JsonValueKind.Array ||
            resultElement.GetArrayLength() == 0)
        {
            return null;
        }

        JsonElement chartResult = resultElement[0];
        if (!chartResult.TryGetProperty("timestamp", out JsonElement timestampsElement) ||
            timestampsElement.ValueKind != JsonValueKind.Array ||
            !chartResult.TryGetProperty("indicators", out JsonElement indicatorsElement) ||
            !indicatorsElement.TryGetProperty("quote", out JsonElement quoteArray) ||
            quoteArray.ValueKind != JsonValueKind.Array ||
            quoteArray.GetArrayLength() == 0 ||
            !quoteArray[0].TryGetProperty("close", out JsonElement closesElement) ||
            closesElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        List<(DateTimeOffset TimestampUtc, decimal Close)> points = [];
        int pointCount = Math.Min(timestampsElement.GetArrayLength(), closesElement.GetArrayLength());
        for (int i = 0; i < pointCount; i++)
        {
            JsonElement closeElement = closesElement[i];
            if (closeElement.ValueKind != JsonValueKind.Number || !closeElement.TryGetDecimal(out decimal close) || close <= 0)
                continue;

            JsonElement timeElement = timestampsElement[i];
            if (timeElement.ValueKind != JsonValueKind.Number || !timeElement.TryGetInt64(out long unixTime) || unixTime <= 0)
                continue;

            points.Add((DateTimeOffset.FromUnixTimeSeconds(unixTime), close));
        }

        if (points.Count == 0)
            return null;

        (DateTimeOffset TimestampUtc, decimal Close) lastPoint = points[^1];
        decimal? previousClose = points.Count > 1 ? points[^2].Close : null;
        decimal? change = previousClose is decimal previousValue
            ? Math.Round(lastPoint.Close - previousValue, 4)
            : null;
        decimal? percent = previousClose is decimal baseline && baseline > 0
            ? Math.Round(((lastPoint.Close - baseline) / baseline) * 100m, 4)
            : null;

        return new QuoteSnapshot
        {
            Symbol = symbol,
            Last = lastPoint.Close,
            Change = change,
            ChangePercent = percent,
            PreviousClose = previousClose,
            ProviderTimestampUtc = lastPoint.TimestampUtc,
            FetchTimestampUtc = DateTimeOffset.UtcNow,
            IsStale = false
        };
    }

    private static bool TryBuildSparkQuoteSnapshot(
        string requestedSymbol,
        JsonElement resultElement,
        out QuoteSnapshot? snapshot)
    {
        snapshot = null;
        if (!TryGetSparkResponse(resultElement, out JsonElement responseElement))
            return false;

        List<(DateTimeOffset TimestampUtc, decimal Close)> points = ExtractSparkSeriesPoints(responseElement);
        JsonElement metaElement = TryGetObject(responseElement, "meta");

        decimal? last = points.Count > 0
            ? points[^1].Close
            : TryGetDecimal(metaElement, "regularMarketPrice");
        decimal? previousClose =
            TryGetDecimal(metaElement, "chartPreviousClose") ??
            TryGetDecimal(metaElement, "regularMarketPreviousClose") ??
            TryGetDecimal(metaElement, "previousClose");
        if (previousClose is null && points.Count > 1)
            previousClose = points[^2].Close;

        if (last is null && previousClose is null)
            return false;

        decimal? change = last is decimal lastValue && previousClose is decimal previousValue
            ? Math.Round(lastValue - previousValue, 4)
            : null;
        decimal? percent = last is decimal latest && previousClose is decimal baseline && baseline > 0
            ? Math.Round(((latest - baseline) / baseline) * 100m, 4)
            : null;
        DateTimeOffset? providerTimestampUtc =
            TryGetUnixTimestamp(metaElement, "regularMarketTime") ??
            (points.Count > 0 ? points[^1].TimestampUtc : null);

        snapshot = new QuoteSnapshot
        {
            Symbol = requestedSymbol,
            Last = last,
            Change = change,
            ChangePercent = percent,
            PreviousClose = previousClose,
            Currency = GetString(metaElement, "currency"),
            ProviderTimestampUtc = providerTimestampUtc,
            FetchTimestampUtc = DateTimeOffset.UtcNow,
            IsStale = false
        };

        return true;
    }

    private static bool TryGetSparkResponse(JsonElement resultElement, out JsonElement responseElement)
    {
        responseElement = default;
        if (!resultElement.TryGetProperty("response", out JsonElement responseArray) ||
            responseArray.ValueKind != JsonValueKind.Array ||
            responseArray.GetArrayLength() == 0)
        {
            return false;
        }

        responseElement = responseArray[0];
        return true;
    }

    private static List<(DateTimeOffset TimestampUtc, decimal Close)> ExtractSparkSeriesPoints(JsonElement responseElement)
    {
        List<(DateTimeOffset TimestampUtc, decimal Close)> points = [];
        if (!responseElement.TryGetProperty("timestamp", out JsonElement timestampsElement) ||
            timestampsElement.ValueKind != JsonValueKind.Array ||
            !responseElement.TryGetProperty("indicators", out JsonElement indicatorsElement) ||
            !indicatorsElement.TryGetProperty("quote", out JsonElement quoteArray) ||
            quoteArray.ValueKind != JsonValueKind.Array ||
            quoteArray.GetArrayLength() == 0 ||
            !quoteArray[0].TryGetProperty("close", out JsonElement closesElement) ||
            closesElement.ValueKind != JsonValueKind.Array)
        {
            return points;
        }

        int pointCount = Math.Min(timestampsElement.GetArrayLength(), closesElement.GetArrayLength());
        for (int i = 0; i < pointCount; i++)
        {
            JsonElement closeElement = closesElement[i];
            if (closeElement.ValueKind != JsonValueKind.Number || !closeElement.TryGetDecimal(out decimal close) || close <= 0)
                continue;

            JsonElement timeElement = timestampsElement[i];
            if (timeElement.ValueKind != JsonValueKind.Number || !timeElement.TryGetInt64(out long unixTime) || unixTime <= 0)
                continue;

            points.Add((DateTimeOffset.FromUnixTimeSeconds(unixTime), close));
        }

        return points;
    }

    private static JsonElement TryGetObject(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out JsonElement propertyElement) && propertyElement.ValueKind == JsonValueKind.Object
            ? propertyElement
            : default;

    private static string GetString(JsonElement element, string propertyName)
        => element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(propertyName, out JsonElement valueElement) &&
           valueElement.ValueKind == JsonValueKind.String
            ? valueElement.GetString() ?? string.Empty
            : string.Empty;

    private static decimal? TryGetDecimal(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out JsonElement valueElement))
        {
            return null;
        }

        return valueElement.ValueKind switch
        {
            JsonValueKind.Number when valueElement.TryGetDecimal(out decimal numeric) => numeric,
            JsonValueKind.String when decimal.TryParse(valueElement.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal parsed) => parsed,
            _ => null
        };
    }

    private static DateTimeOffset? TryGetUnixTimestamp(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out JsonElement valueElement))
        {
            return null;
        }

        if (valueElement.ValueKind == JsonValueKind.Number &&
            valueElement.TryGetInt64(out long unixTime) &&
            unixTime > 0)
        {
            return DateTimeOffset.FromUnixTimeSeconds(unixTime);
        }

        return null;
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

    private static bool IsTooManyRequests(Exception ex)
        => ex is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } ||
           ex.Message.Contains("429", StringComparison.OrdinalIgnoreCase) ||
           ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase);

    private static bool ShouldPreferQuoteEndpointLookup(string symbol)
        => symbol.StartsWith("^", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(symbol, "DX-Y.NYB", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<QuoteSnapshot> BuildOrderedResults(
        IReadOnlyList<string> requestedSymbols,
        IReadOnlyDictionary<string, QuoteSnapshot> snapshots)
        => requestedSymbols
            .Where(symbol => snapshots.ContainsKey(symbol))
            .Select(symbol => snapshots[symbol])
            .ToList();

    private static Exception CreateRateLimitException(
        string message,
        IReadOnlyList<string> requestedSymbols,
        IReadOnlyDictionary<string, QuoteSnapshot> snapshots)
    {
        IReadOnlyList<QuoteSnapshot> partialQuotes = BuildOrderedResults(requestedSymbols, snapshots);
        if (partialQuotes.Count > 0)
        {
            TraceLog.Warn("YahooFinanceQuoteProvider", $"{message} Returning {partialQuotes.Count} partial quotes and deferring the remainder.");
            return new PartialQuoteResultException(message, partialQuotes);
        }

        return new HttpRequestException(message, inner: null, statusCode: HttpStatusCode.TooManyRequests);
    }
}

public sealed class PartialQuoteResultException : HttpRequestException
{
    public PartialQuoteResultException(string message, IReadOnlyList<QuoteSnapshot> partialQuotes)
        : base(message, inner: null, statusCode: HttpStatusCode.TooManyRequests)
    {
        PartialQuotes = partialQuotes;
    }

    public IReadOnlyList<QuoteSnapshot> PartialQuotes { get; }
}
