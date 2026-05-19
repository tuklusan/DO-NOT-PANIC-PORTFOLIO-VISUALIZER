using System.Text.Json;
using YFinance.NET.Config;
using YFinance.NET.Models;
using YFinance.NET.Transport;

namespace YFinance.NET.Features.Quotes;

public sealed class QuoteSummaryService
{
    private readonly YahooFinanceHttpClient _httpClient;
    private readonly YFinanceOptions _options;

    public QuoteSummaryService(YahooFinanceHttpClient httpClient, YFinanceOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public async Task<QuoteSummaryResult?> GetSummaryAsync(string symbol, IEnumerable<string> modules, CancellationToken cancellationToken = default)
    {
        string[] moduleList = modules.Select(static module => module.Trim())
                                     .Where(static module => !string.IsNullOrWhiteSpace(module))
                                     .Distinct(StringComparer.Ordinal)
                                     .ToArray();
        if (moduleList.Length == 0)
        {
            throw new ArgumentException("At least one quote summary module is required.", nameof(modules));
        }

        string normalizedSymbol = symbol.Trim().ToUpperInvariant();
        JsonDocument json = await _httpClient.GetCachedJsonAsync(
            $"/v10/finance/quoteSummary/{Uri.EscapeDataString(normalizedSymbol)}",
            new Dictionary<string, string?>
            {
                ["modules"] = string.Join(',', moduleList),
                ["corsDomain"] = "finance.yahoo.com",
                ["formatted"] = "false",
                ["symbol"] = normalizedSymbol
            },
            _options.SummaryCacheTtl,
            cancellationToken).ConfigureAwait(false);

        JsonElement root = json.RootElement;
        if (!root.TryGetProperty("quoteSummary", out JsonElement quoteSummary) ||
            !quoteSummary.TryGetProperty("result", out JsonElement resultArray) ||
            resultArray.ValueKind != JsonValueKind.Array ||
            resultArray.GetArrayLength() == 0)
        {
            return null;
        }

        JsonElement first = resultArray[0];
        Dictionary<string, JsonElement> mapped = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty property in first.EnumerateObject())
        {
            mapped[property.Name] = property.Value.Clone();
        }

        return new QuoteSummaryResult(normalizedSymbol, mapped, JsonDocument.Parse(json.RootElement.GetRawText()));
    }
}