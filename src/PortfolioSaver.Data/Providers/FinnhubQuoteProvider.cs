using System.Text.Json;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Data.DTOs;
using PortfolioSaver.Data.Interfaces;

namespace PortfolioSaver.Data.Providers;

public sealed class FinnhubQuoteProvider : IQuoteProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    public FinnhubQuoteProvider(HttpClient httpClient, string apiKey)
    {
        _httpClient = httpClient;
        _apiKey = apiKey;
    }

    public async Task<IReadOnlyList<QuoteSnapshot>> GetQuotesAsync(IEnumerable<string> symbols, CancellationToken cancellationToken = default)
    {
        List<QuoteSnapshot> results = [];
        foreach (string symbol in symbols.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(symbol))
                continue;

            if (string.IsNullOrWhiteSpace(_apiKey))
                throw new InvalidOperationException("Finnhub API key is not configured.");

            string url = $"https://finnhub.io/api/v1/quote?symbol={Uri.EscapeDataString(symbol)}&token={Uri.EscapeDataString(_apiKey)}";
            using HttpResponseMessage response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            FinnhubQuoteResponse? dto = await JsonSerializer.DeserializeAsync<FinnhubQuoteResponse>(stream, cancellationToken: cancellationToken);
            QuoteSnapshot snapshot = new()
            {
                Symbol = symbol,
                Last = dto?.Current,
                Change = dto?.Change,
                ChangePercent = dto?.ChangePercent,
                PreviousClose = dto?.PreviousClose,
                ProviderTimestampUtc = dto?.UnixTime is long t && t > 0 ? DateTimeOffset.FromUnixTimeSeconds(t) : null,
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

        string url = $"https://finnhub.io/api/v1/quote?symbol=AAPL&token={Uri.EscapeDataString(_apiKey)}";
        using HttpResponseMessage response = await _httpClient.GetAsync(url, cancellationToken);
        return response.IsSuccessStatusCode;
    }
}
