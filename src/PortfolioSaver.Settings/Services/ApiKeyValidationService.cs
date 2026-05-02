using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.IO;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Data.Services;

namespace PortfolioSaver.Config.Services;

public sealed class ApiKeyValidationService
{
    private const string FinnhubPlaceholder = "abcdefghijklmnopqrstuvwxyz01234567890abc";
    private const string TwelvePlaceholder = "abcdefghijklmnopqrstuvwxyz012345";
    private const string TiingoPlaceholder = "abcdefghijklmnopqrstuvwxyz01234567890abc";
    private const string FmpPlaceholder = "abcdefghijklmnopqrstuvwxyz012345";
    private const string EodhdPlaceholder = "abcdefghijklmn.01234567";

    public async Task<ApiKeyValidationResult> ValidateAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ApiKeyValidationResult result = new();
        Dictionary<string, string> keys = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Finnhub"] = settings.FinnhubApiKey,
            ["Twelve Data"] = settings.TwelveDataApiKey,
            ["Tiingo"] = settings.TiingoApiKey,
            ["Financial Modeling Prep"] = settings.FinancialModelingPrepApiKey,
            ["EODHD"] = settings.EodhdApiKey
        };

        foreach ((string provider, string key) in keys)
        {
            string trimmed = (key ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                result.Errors.Add($"{provider} API key is required.");
                continue;
            }

            if (IsPlaceholder(provider, trimmed))
                result.Errors.Add($"{provider} API key is still set to the installer sample format and must be replaced.");
        }

        if (result.Errors.Count > 0)
            return result;

        using HttpClient client = HttpClientFactory.Create(TimeSpan.FromSeconds(Math.Max(3, settings.HttpTimeoutSeconds)));

        if (!await ValidateFinnhubAsync(client, settings.FinnhubApiKey, cancellationToken))
            result.Errors.Add("Finnhub API key did not validate.");

        if (!await ValidateTwelveDataAsync(client, settings.TwelveDataApiKey, cancellationToken))
            result.Errors.Add("Twelve Data API key did not validate.");

        if (!await ValidateTiingoAsync(client, settings.TiingoApiKey, cancellationToken))
            result.Errors.Add("Tiingo API key did not validate.");

        if (!await ValidateFmpAsync(client, settings.FinancialModelingPrepApiKey, cancellationToken))
            result.Errors.Add("Financial Modeling Prep API key did not validate.");

        if (!await ValidateEodhdAsync(client, settings.EodhdApiKey, cancellationToken))
            result.Errors.Add("EODHD API key did not validate.");

        return result;
    }

    private static bool IsPlaceholder(string provider, string key)
        => provider switch
        {
            "Finnhub" => string.Equals(key, FinnhubPlaceholder, StringComparison.Ordinal),
            "Twelve Data" => string.Equals(key, TwelvePlaceholder, StringComparison.Ordinal),
            "Tiingo" => string.Equals(key, TiingoPlaceholder, StringComparison.Ordinal),
            "Financial Modeling Prep" => string.Equals(key, FmpPlaceholder, StringComparison.Ordinal),
            "EODHD" => string.Equals(key, EodhdPlaceholder, StringComparison.Ordinal),
            _ => false
        };

    private static async Task<bool> ValidateFinnhubAsync(HttpClient client, string key, CancellationToken cancellationToken)
    {
        string url = $"https://finnhub.io/api/v1/quote?symbol=AAPL&token={Uri.EscapeDataString(key.Trim())}";
        using HttpResponseMessage response = await client.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return false;

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return document.RootElement.TryGetProperty("c", out JsonElement currentElement) &&
               currentElement.ValueKind == JsonValueKind.Number;
    }

    private static async Task<bool> ValidateTwelveDataAsync(HttpClient client, string key, CancellationToken cancellationToken)
    {
        string url = $"https://api.twelvedata.com/quote?symbol=AAPL&apikey={Uri.EscapeDataString(key.Trim())}";
        using HttpResponseMessage response = await client.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return false;

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (document.RootElement.TryGetProperty("code", out _))
            return false;

        string? closeText = document.RootElement.TryGetProperty("close", out JsonElement closeElement) ? closeElement.GetString() : null;
        return decimal.TryParse(closeText, NumberStyles.Any, CultureInfo.InvariantCulture, out _);
    }

    private static async Task<bool> ValidateTiingoAsync(HttpClient client, string key, CancellationToken cancellationToken)
    {
        string startDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-7).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string url = $"https://api.tiingo.com/tiingo/daily/AAPL/prices?token={Uri.EscapeDataString(key.Trim())}&startDate={startDate}&resampleFreq=1day";
        using HttpResponseMessage response = await client.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return false;

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return document.RootElement.ValueKind == JsonValueKind.Array && document.RootElement.GetArrayLength() > 0;
    }

    private static async Task<bool> ValidateFmpAsync(HttpClient client, string key, CancellationToken cancellationToken)
    {
        string url = $"https://financialmodelingprep.com/api/v3/market-hours?apikey={Uri.EscapeDataString(key.Trim())}";
        using HttpResponseMessage response = await client.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return false;

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (body.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
            body.Contains("Invalid", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(body);
    }

    private static async Task<bool> ValidateEodhdAsync(HttpClient client, string key, CancellationToken cancellationToken)
    {
        string url = $"https://eodhd.com/api/exchange-details/US?api_token={Uri.EscapeDataString(key.Trim())}&fmt=json";
        using HttpResponseMessage response = await client.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return false;

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            return false;

        if (document.RootElement.TryGetProperty("error", out _))
            return false;

        return document.RootElement.TryGetProperty("Code", out JsonElement codeElement) &&
               codeElement.ValueKind == JsonValueKind.String &&
               !string.IsNullOrWhiteSpace(codeElement.GetString());
    }
}

public sealed class ApiKeyValidationResult
{
    public List<string> Errors { get; } = [];
    public bool IsValid => Errors.Count == 0;
}
