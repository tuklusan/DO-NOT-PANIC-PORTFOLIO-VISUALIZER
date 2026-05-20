using System.Text.Json;
using YFinance.NET.Config;
using YFinance.NET.Diagnostics;
using YFinance.NET.Models;
using YFinance.NET.Transport;

namespace YFinance.NET.Features.Quotes;

public sealed class QuoteSummaryService
{
    private readonly YahooFinanceHttpClient _httpClient;
    private readonly YFinanceOptions _options;
    private readonly YFinanceTrace _trace;

    public QuoteSummaryService(YahooFinanceHttpClient httpClient, YFinanceOptions options, YFinanceTrace? trace = null)
    {
        _httpClient = httpClient;
        _options = options;
        _trace = trace ?? new YFinanceTrace(options.TraceSink);
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
        _trace.InfoState("YFinance.Summary", "SummaryRequestStart", ("symbol", normalizedSymbol), ("modules", moduleList), ("module_count", moduleList.Length));
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
            _trace.WarnState("YFinance.Summary", "SummaryRequestEmpty", ("symbol", normalizedSymbol), ("modules", moduleList));
            return null;
        }

        JsonElement first = resultArray[0];
        Dictionary<string, JsonElement> mapped = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty property in first.EnumerateObject())
        {
            mapped[property.Name] = property.Value.Clone();
        }

        _trace.InfoState("YFinance.Summary", "SummaryRequestComplete", ("symbol", normalizedSymbol), ("module_count", mapped.Count));
        return new QuoteSummaryResult(normalizedSymbol, mapped, JsonDocument.Parse(json.RootElement.GetRawText()));
    }
}
