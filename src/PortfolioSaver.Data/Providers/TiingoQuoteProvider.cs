using System.Globalization;
using System.Text.Json;
using System.Net;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Data.Interfaces;

namespace PortfolioSaver.Data.Providers;

public sealed class TiingoQuoteProvider : IQuoteProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    public TiingoQuoteProvider(HttpClient httpClient, string apiKey)
    {
        _httpClient = httpClient;
        _apiKey = apiKey ?? string.Empty;
    }

    public async Task<IReadOnlyList<QuoteSnapshot>> GetQuotesAsync(IEnumerable<string> symbols, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new InvalidOperationException("Tiingo API key is not configured.");

        List<QuoteSnapshot> results = [];
        foreach (string symbol in symbols.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(symbol))
                continue;

            try
            {
                QuoteSnapshot? snapshot = await FetchDailyQuoteAsync(symbol, cancellationToken)
                    ?? await FetchIexQuoteAsync(symbol, cancellationToken);
                if (snapshot is not null)
                    results.Add(snapshot);
            }
            catch (HttpRequestException ex) when (ShouldSkipSymbol(ex))
            {
                continue;
            }
        }

        if (results.Count == 0)
            throw new InvalidOperationException("Tiingo returned no matching quotes.");

        return results;
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            return false;

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

    private async Task<QuoteSnapshot?> FetchIexQuoteAsync(string symbol, CancellationToken cancellationToken)
    {
        string url = $"https://api.tiingo.com/iex/?tickers={Uri.EscapeDataString(symbol)}&token={Uri.EscapeDataString(_apiKey)}";
        using HttpResponseMessage response = await _httpClient.GetAsync(url, cancellationToken);
        if (ShouldSkipSymbol(response.StatusCode))
            return null;

        response.EnsureSuccessStatusCode();

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0)
            return null;

        JsonElement quoteElement = document.RootElement[0];
        decimal? last = TryGetDecimal(quoteElement, "last") ?? TryGetDecimal(quoteElement, "tngoLast");
        decimal? previousClose = TryGetDecimal(quoteElement, "prevClose");
        if (last is null && previousClose is null)
            return null;

        DateTimeOffset? providerTimestampUtc = TryGetTimestamp(quoteElement, "quoteTimestamp")
            ?? TryGetTimestamp(quoteElement, "timestamp");
        decimal? change = last is decimal lastValue && previousClose is decimal previousValue
            ? Math.Round(lastValue - previousValue, 4)
            : null;
        decimal? changePercent = last is decimal changedLast && previousClose is decimal baseline && baseline > 0
            ? Math.Round(((changedLast - baseline) / baseline) * 100m, 4)
            : null;

        return new QuoteSnapshot
        {
            Symbol = symbol,
            Last = last,
            Change = change,
            ChangePercent = changePercent,
            PreviousClose = previousClose,
            ProviderTimestampUtc = providerTimestampUtc,
            FetchTimestampUtc = DateTimeOffset.UtcNow,
            IsStale = false
        };
    }

    private async Task<QuoteSnapshot?> FetchDailyQuoteAsync(string symbol, CancellationToken cancellationToken)
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        string startDate = today.AddDays(-7).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string url = $"https://api.tiingo.com/tiingo/daily/{Uri.EscapeDataString(symbol)}/prices?token={Uri.EscapeDataString(_apiKey)}&startDate={startDate}&resampleFreq=1day";
        using HttpResponseMessage response = await _httpClient.GetAsync(url, cancellationToken);
        if (ShouldSkipSymbol(response.StatusCode))
            return null;

        response.EnsureSuccessStatusCode();

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0)
            return null;

        List<(DateTimeOffset TimestampUtc, decimal? Close)> points = [];
        foreach (JsonElement pointElement in document.RootElement.EnumerateArray())
        {
            decimal? close = TryGetDecimal(pointElement, "close") ?? TryGetDecimal(pointElement, "adjClose");
            DateTimeOffset? timestampUtc = TryGetTimestamp(pointElement, "date");
            if (timestampUtc is null)
                continue;

            points.Add((timestampUtc.Value, close));
        }

        if (points.Count == 0)
            return null;

        (DateTimeOffset TimestampUtc, decimal? Close) lastPoint = points[^1];
        JsonElement lastElement = document.RootElement[document.RootElement.GetArrayLength() - 1];
        decimal? previousClose = points.Count > 1
            ? points[^2].Close
            : TryGetDecimal(lastElement, "prevClose");

        decimal? last = lastPoint.Close;
        decimal? change = last is decimal lastValue && previousClose is decimal previousValue
            ? Math.Round(lastValue - previousValue, 4)
            : null;
        decimal? changePercent = last is decimal latest && previousClose is decimal baseline && baseline > 0
            ? Math.Round(((latest - baseline) / baseline) * 100m, 4)
            : null;

        return new QuoteSnapshot
        {
            Symbol = symbol,
            Last = last,
            Change = change,
            ChangePercent = changePercent,
            PreviousClose = previousClose,
            ProviderTimestampUtc = lastPoint.TimestampUtc,
            FetchTimestampUtc = DateTimeOffset.UtcNow,
            IsStale = false
        };
    }

    private static decimal? TryGetDecimal(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDecimal(out decimal number) => number,
            JsonValueKind.String when decimal.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal parsed) => parsed,
            _ => null
        };
    }

    private static DateTimeOffset? TryGetTimestamp(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value))
            return null;

        if (value.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset parsed))
        {
            return parsed;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long unixSeconds) && unixSeconds > 0)
            return DateTimeOffset.FromUnixTimeSeconds(unixSeconds);

        return null;
    }

    private static bool ShouldSkipSymbol(HttpRequestException ex)
        => ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest;

    private static bool ShouldSkipSymbol(HttpStatusCode statusCode)
        => statusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest;
}
