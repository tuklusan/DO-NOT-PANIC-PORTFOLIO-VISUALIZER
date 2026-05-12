using System.Globalization;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Data.Interfaces;

namespace PortfolioSaver.Data.Providers;

public sealed class StooqGlobalMarketQuoteProvider : IQuoteProvider
{
    private static readonly IReadOnlyDictionary<string, string> StooqSymbolsByCanonicalSymbol =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["DX-Y.NYB"] = "dx.f",
            ["CL=F"] = "cl.f",
            ["BZ=F"] = "cb.f",
            ["GC=F"] = "gc.f",
            ["^SPX"] = "^spx",
            ["^FTSE"] = "^ukx",
            ["^N225"] = "^nkx",
            ["^SSEC"] = "^shc",
            ["^HSI"] = "^hsi",
            ["INDY.US"] = "indy.us",
            ["^GDAXI"] = "^dax",
            ["^FCHI"] = "^cac",
            ["^GSPTSE"] = "^tsx",
            ["^KS11"] = "^kospi",
            ["EWA.US"] = "ewa.us"
        };

    private readonly HttpClient _httpClient;

    public StooqGlobalMarketQuoteProvider(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public static bool CanResolve(string? symbol)
        => !string.IsNullOrWhiteSpace(symbol) &&
           StooqSymbolsByCanonicalSymbol.ContainsKey(symbol.Trim().ToUpperInvariant());

    public async Task<IReadOnlyList<QuoteSnapshot>> GetQuotesAsync(IEnumerable<string> symbols, CancellationToken cancellationToken = default)
    {
        List<QuoteSnapshot> results = [];
        foreach (string symbol in symbols.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!StooqSymbolsByCanonicalSymbol.TryGetValue(symbol, out string? stooqSymbol))
                continue;

            QuoteSnapshot? snapshot = await FetchQuoteAsync(symbol, stooqSymbol, cancellationToken);
            if (snapshot is not null)
                results.Add(snapshot);
        }

        return results;
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<QuoteSnapshot> quotes = await GetQuotesAsync(["DX-Y.NYB"], cancellationToken);
        return quotes.Count > 0;
    }

    private async Task<QuoteSnapshot?> FetchQuoteAsync(string canonicalSymbol, string stooqSymbol, CancellationToken cancellationToken)
    {
        string url = $"https://stooq.com/q/l/?s={Uri.EscapeDataString(stooqSymbol)}&f=sd2t2ohlcvn&h&e=csv";
        using HttpResponseMessage response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        string payload = await response.Content.ReadAsStringAsync(cancellationToken);
        using StringReader reader = new(payload);
        _ = reader.ReadLine();
        string? dataLine = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(dataLine))
            return null;

        return TryParseCsvQuote(canonicalSymbol, dataLine);
    }

    private static QuoteSnapshot? TryParseCsvQuote(string canonicalSymbol, string dataLine)
    {
        string[] fields = dataLine.Split(',');
        if (fields.Length < 7 || IsNoData(fields[1]) || IsNoData(fields[6]))
            return null;

        decimal? open = ParseDecimal(fields[3]);
        decimal? close = ParseDecimal(fields[6]);
        if (close is null)
            return null;

        decimal? change = open.HasValue ? Math.Round(close.Value - open.Value, 4) : null;
        decimal? changePercent = open is decimal openValue && openValue != 0
            ? Math.Round(((close.Value - openValue) / openValue) * 100m, 4)
            : null;

        DateTimeOffset? providerTimestamp = TryParseProviderTimestamp(fields[1], fields.Length > 2 ? fields[2] : null);
        return new QuoteSnapshot
        {
            Symbol = canonicalSymbol,
            Last = close,
            PreviousClose = open,
            Change = change,
            ChangePercent = changePercent,
            ProviderTimestampUtc = providerTimestamp,
            FetchTimestampUtc = DateTimeOffset.UtcNow,
            IsStale = false
        };
    }

    private static bool IsNoData(string? value)
        => string.IsNullOrWhiteSpace(value) ||
           string.Equals(value.Trim(), "N/D", StringComparison.OrdinalIgnoreCase);

    private static decimal? ParseDecimal(string? value)
        => decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal parsed) ? parsed : null;

    private static DateTimeOffset? TryParseProviderTimestamp(string dateText, string? timeText)
    {
        string raw = $"{dateText.Trim()} {(timeText ?? string.Empty).Trim()}".Trim();
        string[] formats = ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd"];
        return DateTime.TryParseExact(
                raw,
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTime parsed)
            ? new DateTimeOffset(parsed, TimeSpan.Zero)
            : null;
    }
}
