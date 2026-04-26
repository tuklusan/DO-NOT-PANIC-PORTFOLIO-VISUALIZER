using System.Globalization;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Data.Interfaces;

namespace PortfolioSaver.Data.Providers;

public sealed class CboeVolatilityIndexQuoteProvider : IQuoteProvider
{
    private static readonly IReadOnlyDictionary<string, string> HistoryFileBySymbol = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["^VIX"] = "VIX_History.csv",
        ["^VIX3M"] = "VIX3M_History.csv"
    };

    private readonly HttpClient _httpClient;

    public CboeVolatilityIndexQuoteProvider(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<IReadOnlyList<QuoteSnapshot>> GetQuotesAsync(IEnumerable<string> symbols, CancellationToken cancellationToken = default)
    {
        List<QuoteSnapshot> results = [];
        foreach (string symbol in symbols.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!HistoryFileBySymbol.TryGetValue(symbol, out string? historyFile))
                continue;

            QuoteSnapshot? snapshot = await FetchHistorySnapshotAsync(symbol, historyFile, cancellationToken);
            if (snapshot is not null)
                results.Add(snapshot);
        }

        return results;
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<QuoteSnapshot> quotes = await GetQuotesAsync(["^VIX"], cancellationToken);
        return quotes.Count > 0;
    }

    private async Task<QuoteSnapshot?> FetchHistorySnapshotAsync(string symbol, string historyFile, CancellationToken cancellationToken)
    {
        string url = $"https://cdn.cboe.com/api/global/us_indices/daily_prices/{Uri.EscapeDataString(historyFile)}";
        using HttpResponseMessage response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        string payload = await response.Content.ReadAsStringAsync(cancellationToken);
        List<(DateTime Date, decimal Close)> observations = ParseDailyCloseObservations(payload);
        if (observations.Count == 0)
            return null;

        (DateTime Date, decimal Close) last = observations[^1];
        decimal? previousClose = observations.Count > 1 ? observations[^2].Close : null;
        decimal? change = previousClose.HasValue
            ? Math.Round(last.Close - previousClose.Value, 4)
            : null;
        decimal? changePercent = previousClose is decimal previous && previous != 0
            ? Math.Round(((last.Close - previous) / previous) * 100m, 4)
            : null;

        return new QuoteSnapshot
        {
            Symbol = symbol,
            Last = last.Close,
            PreviousClose = previousClose,
            Change = change,
            ChangePercent = changePercent,
            ProviderTimestampUtc = new DateTimeOffset(last.Date, TimeSpan.Zero),
            FetchTimestampUtc = DateTimeOffset.UtcNow,
            IsStale = false
        };
    }

    private static List<(DateTime Date, decimal Close)> ParseDailyCloseObservations(string payload)
    {
        List<(DateTime Date, decimal Close)> observations = [];
        using StringReader reader = new(payload);
        string? header = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(header))
            return observations;

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            string[] fields = line.Split(',');
            if (fields.Length < 5)
                continue;

            if (!DateTime.TryParseExact(
                    fields[0].Trim(),
                    "MM/dd/yyyy",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out DateTime date))
            {
                continue;
            }

            if (!decimal.TryParse(fields[4].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal close))
                continue;

            observations.Add((date.Date, close));
        }

        return observations
            .OrderBy(point => point.Date)
            .ToList();
    }
}
