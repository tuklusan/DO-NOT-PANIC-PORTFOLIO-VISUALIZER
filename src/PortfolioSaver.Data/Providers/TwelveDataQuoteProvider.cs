using System.Globalization;
using System.Text.Json;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Core.Enums;
using PortfolioSaver.Core.Services;
using PortfolioSaver.Data.DTOs;
using PortfolioSaver.Data.Interfaces;

namespace PortfolioSaver.Data.Providers;

public sealed class TwelveDataQuoteProvider : IQuoteProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    public TwelveDataQuoteProvider(HttpClient? httpClient = null, string? apiKey = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _apiKey = apiKey ?? string.Empty;
    }

    public async Task<IReadOnlyList<QuoteSnapshot>> GetQuotesAsync(IEnumerable<string> symbols, CancellationToken cancellationToken = default)
    {
        List<QuoteSnapshot> results = [];
        foreach (string symbol in symbols.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(symbol))
                continue;

            if (string.IsNullOrWhiteSpace(_apiKey))
                throw new InvalidOperationException("Twelve Data API key is not configured.");

            string requestSymbol = ProviderSymbolAliasCatalog.GetQuoteRequestSymbol(DataSourceKind.TwelveData, symbol);
            string url = $"https://api.twelvedata.com/quote?symbol={Uri.EscapeDataString(requestSymbol)}&apikey={Uri.EscapeDataString(_apiKey)}";
            using HttpResponseMessage response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (document.RootElement.TryGetProperty("code", out JsonElement codeElement))
            {
                string message = document.RootElement.TryGetProperty("message", out JsonElement messageElement)
                    ? messageElement.GetString() ?? $"Twelve Data quote failed for {symbol}."
                    : $"Twelve Data quote failed for {symbol}.";
                if (ShouldSkipSymbol(codeElement, message))
                    continue;

                throw new InvalidOperationException(message);
            }

            TwelveDataQuoteResponse? dto = document.RootElement.Deserialize<TwelveDataQuoteResponse>();
            decimal? last = ParseDecimal(dto?.Close);
            decimal? previousClose = ParseDecimal(dto?.PreviousClose);
            decimal? changePercent = ParseDecimal(dto?.PercentChange);
            decimal? change = last is decimal lastValue && previousClose is decimal previousValue
                ? Math.Round(lastValue - previousValue, 4)
                : null;

            QuoteSnapshot snapshot = new()
            {
                Symbol = symbol,
                Last = last,
                Change = change,
                ChangePercent = changePercent,
                PreviousClose = previousClose,
                FetchTimestampUtc = DateTimeOffset.UtcNow,
                IsStale = false
            };

            if (snapshot.Last is null && snapshot.PreviousClose is null)
                continue;

            results.Add(snapshot);
        }

        return results;
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            return false;

        string url = $"https://api.twelvedata.com/quote?symbol=AAPL&apikey={Uri.EscapeDataString(_apiKey)}";
        using HttpResponseMessage response = await _httpClient.GetAsync(url, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    private static bool ShouldSkipSymbol(JsonElement codeElement, string message)
    {
        string code = codeElement.ValueKind switch
        {
            JsonValueKind.Number => codeElement.ToString(),
            JsonValueKind.String => codeElement.GetString() ?? string.Empty,
            _ => string.Empty
        };

        return string.Equals(code, "400", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(code, "404", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("symbol is not found", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("no data", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("invalid symbol", StringComparison.OrdinalIgnoreCase);
    }

    private static decimal? ParseDecimal(string? value)
        => decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal parsed) ? parsed : null;
}
