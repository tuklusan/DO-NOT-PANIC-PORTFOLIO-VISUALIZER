using System.Text.Json;
using System.Net.Http;
using System.IO;
using PortfolioSaver.Data.Services;

namespace PortfolioSaver.Config.Services;

public sealed class YahooSymbolValidationService
{
    private const int MaxBatchSymbols = 8;
    private static readonly TimeSpan InterBatchDelay = TimeSpan.FromSeconds(2);

    public async Task<YahooSymbolValidationResult> ValidateAsync(
        IEnumerable<string> symbols,
        int timeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        List<string> normalizedSymbols = symbols
            .Select(Normalize)
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        YahooSymbolValidationResult result = new(normalizedSymbols);
        if (normalizedSymbols.Count == 0)
            return result;

        using HttpClient httpClient = HttpClientFactory.Create(TimeSpan.FromSeconds(Math.Max(3, timeoutSeconds)));
        YahooFinanceSessionService sessionService = new(httpClient);

        IReadOnlyList<List<string>> batches = ChunkSymbols(normalizedSymbols, MaxBatchSymbols).ToList();
        for (int batchIndex = 0; batchIndex < batches.Count; batchIndex++)
        {
            List<string> batch = batches[batchIndex];
            cancellationToken.ThrowIfCancellationRequested();
            string symbolsCsv = string.Join(",", batch.Select(Uri.EscapeDataString));
            string url = $"https://query1.finance.yahoo.com/v8/finance/spark?symbols={symbolsCsv}&range=7d&interval=1d&includePrePost=false";
            using HttpResponseMessage response = await sessionService.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("spark", out JsonElement sparkElement) ||
                !sparkElement.TryGetProperty("result", out JsonElement resultArray) ||
                resultArray.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (JsonElement entry in resultArray.EnumerateArray())
            {
                string symbol = GetString(entry, "symbol");
                if (string.IsNullOrWhiteSpace(symbol))
                    continue;

                if (!entry.TryGetProperty("response", out JsonElement responseArray) ||
                    responseArray.ValueKind != JsonValueKind.Array ||
                    responseArray.GetArrayLength() == 0)
                {
                    continue;
                }

                JsonElement responseElement = responseArray[0];
                JsonElement metaElement = responseElement.TryGetProperty("meta", out JsonElement meta) && meta.ValueKind == JsonValueKind.Object
                    ? meta
                    : default;

                bool hasLiveData = HasAnyDataPoint(responseElement) ||
                                   TryGetDecimal(metaElement, "regularMarketPrice") is decimal;
                if (!hasLiveData)
                    continue;

                string normalized = Normalize(symbol);
                result.MarkValid(
                    normalized,
                    GetString(metaElement, "shortName"),
                    GetString(metaElement, "longName"));
            }

            if (batchIndex < batches.Count - 1)
                await Task.Delay(InterBatchDelay, cancellationToken);
        }

        return result;
    }

    private static IEnumerable<List<string>> ChunkSymbols(IReadOnlyList<string> symbols, int size)
    {
        if (size <= 0)
            yield break;

        for (int index = 0; index < symbols.Count; index += size)
            yield return symbols.Skip(index).Take(Math.Min(size, symbols.Count - index)).ToList();
    }

    private static string Normalize(string? symbol)
        => (symbol ?? string.Empty).Trim().ToUpperInvariant();

    private static string GetString(JsonElement element, string propertyName)
        => element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(propertyName, out JsonElement propertyElement) &&
           propertyElement.ValueKind == JsonValueKind.String
            ? propertyElement.GetString() ?? string.Empty
            : string.Empty;

    private static decimal? TryGetDecimal(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out decimal number)
            ? number
            : null;
    }

    private static bool HasAnyDataPoint(JsonElement responseElement)
    {
        if (!responseElement.TryGetProperty("timestamp", out JsonElement timestampsElement) ||
            timestampsElement.ValueKind != JsonValueKind.Array ||
            !responseElement.TryGetProperty("indicators", out JsonElement indicatorsElement) ||
            !indicatorsElement.TryGetProperty("quote", out JsonElement quoteArray) ||
            quoteArray.ValueKind != JsonValueKind.Array ||
            quoteArray.GetArrayLength() == 0 ||
            !quoteArray[0].TryGetProperty("close", out JsonElement closesElement) ||
            closesElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        int count = Math.Min(timestampsElement.GetArrayLength(), closesElement.GetArrayLength());
        for (int i = 0; i < count; i++)
        {
            if (closesElement[i].ValueKind == JsonValueKind.Number &&
                closesElement[i].TryGetDecimal(out decimal close) &&
                close > 0)
            {
                return true;
            }
        }

        return false;
    }
}

public sealed class YahooSymbolValidationResult
{
    private readonly Dictionary<string, YahooSymbolValidationEntry> _entries;

    public YahooSymbolValidationResult(IEnumerable<string> requestedSymbols)
    {
        _entries = requestedSymbols
            .Select(symbol => Normalize(symbol))
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                symbol => symbol,
                symbol => new YahooSymbolValidationEntry(symbol),
                StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyDictionary<string, YahooSymbolValidationEntry> Entries => _entries;

    public IReadOnlyList<string> InvalidSymbols => _entries.Values
        .Where(entry => !entry.IsValid)
        .Select(entry => entry.Symbol)
        .OrderBy(symbol => symbol, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public void MarkValid(string symbol, string? shortName, string? longName)
    {
        string normalized = Normalize(symbol);
        if (!_entries.TryGetValue(normalized, out YahooSymbolValidationEntry? entry))
            return;

        entry.IsValid = true;
        entry.DisplayName = !string.IsNullOrWhiteSpace(shortName)
            ? shortName!.Trim()
            : (!string.IsNullOrWhiteSpace(longName) ? longName!.Trim() : entry.DisplayName);
    }

    private static string Normalize(string? symbol)
        => (symbol ?? string.Empty).Trim().ToUpperInvariant();
}

public sealed class YahooSymbolValidationEntry
{
    public YahooSymbolValidationEntry(string symbol)
    {
        Symbol = symbol;
        IsValid = false;
    }

    public string Symbol { get; }
    public bool IsValid { get; set; }
    public string DisplayName { get; set; } = string.Empty;
}
