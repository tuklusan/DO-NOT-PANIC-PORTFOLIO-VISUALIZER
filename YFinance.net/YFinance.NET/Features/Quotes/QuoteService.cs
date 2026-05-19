using System.Text.Json;
using YFinance.NET.Models;
using YFinance.NET.Transport;

namespace YFinance.NET.Features.Quotes;

public sealed class QuoteService
{
    private readonly YahooFinanceHttpClient _httpClient;

    public QuoteService(YahooFinanceHttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<QuoteSnapshot?> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<string, QuoteSnapshot> results = await GetQuotesAsync(new[] { symbol }, cancellationToken).ConfigureAwait(false);
        return results.TryGetValue(symbol.ToUpperInvariant(), out QuoteSnapshot? snapshot) ? snapshot : null;
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

        JsonDocument json = await _httpClient.GetJsonAsync(
            "/v7/finance/quote",
            new Dictionary<string, string?>
            {
                ["symbols"] = string.Join(',', normalized),
                ["formatted"] = "false"
            },
            cancellationToken).ConfigureAwait(false);

        Dictionary<string, QuoteSnapshot> results = new(StringComparer.OrdinalIgnoreCase);
        JsonElement root = json.RootElement;
        if (!root.TryGetProperty("quoteResponse", out JsonElement quoteResponse) ||
            !quoteResponse.TryGetProperty("result", out JsonElement resultArray))
        {
            return results;
        }

        foreach (JsonElement item in resultArray.EnumerateArray())
        {
            string? symbol = item.TryGetProperty("symbol", out JsonElement symbolValue) ? symbolValue.GetString() : null;
            if (string.IsNullOrWhiteSpace(symbol)) { continue; }
            results[symbol.ToUpperInvariant()] = CreateSnapshot(item, symbol);
        }

        return results;
    }

    private static QuoteSnapshot CreateSnapshot(JsonElement item, string symbol)
    {
        return new QuoteSnapshot(
            Symbol: symbol.ToUpperInvariant(),
            ShortName: GetString(item, "shortName"),
            LongName: GetString(item, "longName"),
            Currency: GetString(item, "currency"),
            Exchange: GetString(item, "fullExchangeName") ?? GetString(item, "exchange"),
            QuoteType: GetString(item, "quoteType"),
            RegularMarketPrice: GetDecimal(item, "regularMarketPrice"),
            RegularMarketPreviousClose: GetDecimal(item, "regularMarketPreviousClose"),
            RegularMarketChangePercent: GetDecimal(item, "regularMarketChangePercent"),
            MarketCap: GetLong(item, "marketCap"),
            Raw: item.Clone());
    }

    private static string? GetString(JsonElement item, string propertyName)
        => item.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static decimal? GetDecimal(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out JsonElement value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out decimal result)) return result;
        if (value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), out result)) return result;
        return null;
    }

    private static long? GetLong(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out JsonElement value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long result)) return result;
        if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out result)) return result;
        return null;
    }
}
