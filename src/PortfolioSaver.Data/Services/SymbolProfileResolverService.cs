using System.Net;
using System.Text.Json;
using PortfolioSaver.Core.Constants;
using PortfolioSaver.Core.Enums;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Core.Services;
using PortfolioSaver.Data.Interfaces;
using PortfolioSaver.Data.Providers;

namespace PortfolioSaver.Data.Services;

public sealed class SymbolProfileResolverService
{
    private readonly HttpClient _httpClient;
    private readonly YahooFinanceSessionService _yahooSessionService;
    private readonly SymbolNormalizer _symbolNormalizer = new();

    public SymbolProfileResolverService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _yahooSessionService = new YahooFinanceSessionService(_httpClient);
    }

    public async Task<ResolvedSymbolProfile> ResolveAsync(
        string symbol,
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        string normalizedSymbol = _symbolNormalizer.Normalize(symbol);
        SymbolProfile profile = new()
        {
            Symbol = normalizedSymbol,
            CanonicalSymbol = normalizedSymbol,
            AssetClass = SymbolProfileHeuristics.InferAssetClass(normalizedSymbol),
            LastValidatedUtc = DateTimeOffset.UtcNow
        };

        YahooSymbolMetadata? yahooMetadata = await TryGetYahooMetadataAsync(normalizedSymbol, cancellationToken);
        ApplyMetadata(profile, yahooMetadata);

        bool sawAvailabilityIssue = false;
        bool sawAttempt = false;

        foreach (ProviderEntry provider in BuildProviders(settings))
        {
            if (!DataSourceSymbolEligibility.IsEligible(provider.Kind, normalizedSymbol, profile))
                continue;

            sawAttempt = true;
            try
            {
                IReadOnlyList<QuoteSnapshot> quotes = await provider.Provider.GetQuotesAsync([normalizedSymbol], cancellationToken);
                if (!quotes.Any(HasUsableQuote))
                    continue;

                profile.SupportedQuoteSources.Add(provider.Kind);
            }
            catch (Exception ex) when (IsDefiniteInvalid(ex))
            {
            }
            catch (Exception ex) when (IsProviderAvailabilityIssue(ex))
            {
                sawAvailabilityIssue = true;
            }
        }

        profile.SupportedQuoteSources = profile.SupportedQuoteSources
            .Distinct()
            .OrderBy(kind => (int)kind)
            .ToList();
        profile.SupportedHistorySources = InferHistorySources(profile);

        if (profile.SupportedQuoteSources.Count > 0)
        {
            profile.ValidationSummary = $"Validated via {string.Join(", ", profile.SupportedQuoteSources.Select(GetDisplayName))}.";
            return ResolvedSymbolProfile.Valid(profile);
        }

        if (sawAvailabilityIssue)
        {
            profile.ValidationSummary = "Validation was inconclusive because one or more providers were unavailable or rate-limited.";
            return ResolvedSymbolProfile.Indeterminate(
                profile,
                $"Could not fully validate '{normalizedSymbol}' because one or more data sources were unavailable or rate-limited.");
        }

        string invalidMessage = !sawAttempt && profile.AssetClass != SymbolAssetClass.Unknown
            ? $"'{normalizedSymbol}' is not supported by the currently enabled quote providers."
            : $"'{normalizedSymbol}' could not be validated against the currently enabled quote providers.";
        profile.ValidationSummary = invalidMessage;
        return ResolvedSymbolProfile.Invalid(profile, invalidMessage);
    }

    private IReadOnlyList<ProviderEntry> BuildProviders(AppSettings settings)
    {
        List<ProviderEntry> providers = [];
        foreach (DataSourcePolicySettings policy in settings.DataSources)
        {
            if (!policy.EnableSingleTickerQueries && !policy.EnableBatchTickerQueries)
                continue;

            switch (policy.Kind)
            {
                case DataSourceKind.Finnhub when !string.IsNullOrWhiteSpace(settings.FinnhubApiKey):
                    providers.Add(new ProviderEntry(DataSourceKind.Finnhub, new FinnhubQuoteProvider(_httpClient, settings.FinnhubApiKey)));
                    break;
                case DataSourceKind.TwelveData when !string.IsNullOrWhiteSpace(settings.TwelveDataApiKey):
                    providers.Add(new ProviderEntry(DataSourceKind.TwelveData, new TwelveDataQuoteProvider(_httpClient, settings.TwelveDataApiKey)));
                    break;
                case DataSourceKind.Tiingo when !string.IsNullOrWhiteSpace(settings.TiingoApiKey):
                    providers.Add(new ProviderEntry(DataSourceKind.Tiingo, new TiingoQuoteProvider(_httpClient, settings.TiingoApiKey)));
                    break;
                case DataSourceKind.YahooFinance:
                    providers.Add(new ProviderEntry(DataSourceKind.YahooFinance, new YahooFinanceQuoteProvider(_httpClient, _yahooSessionService)));
                    break;
            }
        }

        return providers;
    }

    private async Task<YahooSymbolMetadata?> TryGetYahooMetadataAsync(string symbol, CancellationToken cancellationToken)
    {
        string url = $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}?interval=1d&range=7d&includePrePost=false";
        using HttpResponseMessage response = await _yahooSessionService.GetAsync(url, cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
            return null;

        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("chart", out JsonElement chartElement) ||
            !chartElement.TryGetProperty("result", out JsonElement resultElement) ||
            resultElement.ValueKind != JsonValueKind.Array ||
            resultElement.GetArrayLength() == 0)
        {
            return null;
        }

        JsonElement result = resultElement[0];
        if (!result.TryGetProperty("meta", out JsonElement metaElement))
            return null;

        return new YahooSymbolMetadata(
            Symbol: GetString(metaElement, "symbol"),
            DisplayName: GetString(metaElement, "shortName") ?? GetString(metaElement, "longName"),
            Exchange: GetString(metaElement, "exchangeName"),
            Currency: GetString(metaElement, "currency"),
            InstrumentType: GetString(metaElement, "instrumentType"));
    }

    private static void ApplyMetadata(SymbolProfile profile, YahooSymbolMetadata? metadata)
    {
        if (metadata is null)
            return;

        if (!string.IsNullOrWhiteSpace(metadata.Symbol))
            profile.CanonicalSymbol = SymbolProfileHeuristics.Normalize(metadata.Symbol);

        if (!string.IsNullOrWhiteSpace(metadata.DisplayName))
            profile.DisplayName = metadata.DisplayName.Trim();

        profile.Exchange = metadata.Exchange?.Trim() ?? string.Empty;
        profile.Currency = metadata.Currency?.Trim() ?? string.Empty;
        profile.RawInstrumentType = metadata.InstrumentType?.Trim() ?? string.Empty;

        SymbolAssetClass inferred = SymbolProfileHeuristics.InferAssetClass(profile.Symbol, profile.RawInstrumentType);
        if (inferred != SymbolAssetClass.Unknown)
            profile.AssetClass = inferred;
    }

    private static bool HasUsableQuote(QuoteSnapshot quote)
        => (quote.Last is decimal last && last > 0) ||
           (quote.PreviousClose is decimal previousClose && previousClose > 0);

    private static bool IsDefiniteInvalid(Exception ex)
    {
        string message = ex.Message;
        return ex is InvalidOperationException &&
               (message.Contains("invalid", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("no matching quotes", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("no data", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("missing or invalid", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsProviderAvailabilityIssue(Exception ex)
        => ex is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } ||
           ex is TaskCanceledException ||
           ex is HttpRequestException ||
           ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ||
           ex.Message.Contains("run out of API credits", StringComparison.OrdinalIgnoreCase) ||
           ex.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase);

    private static string? GetString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string GetDisplayName(DataSourceKind kind)
        => DataSourceCatalog.GetCapabilities(kind).DisplayName;

    private static List<DataSourceKind> InferHistorySources(SymbolProfile profile)
    {
        List<DataSourceKind> kinds = [];
        foreach (DataSourceKind kind in DataSourceCatalog.OrderedKinds)
        {
            if (!DataSourceSymbolEligibility.IsHistoryEligible(kind, profile.Symbol, profile))
                continue;

            kinds.Add(kind);
        }

        return kinds
            .Distinct()
            .OrderBy(kind => (int)kind)
            .ToList();
    }

    private sealed record ProviderEntry(DataSourceKind Kind, IQuoteProvider Provider);

    private sealed record YahooSymbolMetadata(
        string? Symbol,
        string? DisplayName,
        string? Exchange,
        string? Currency,
        string? InstrumentType);
}

public readonly record struct ResolvedSymbolProfile(
    SymbolProfile Profile,
    SymbolProfileResolutionStatus Status,
    string Message)
{
    public static ResolvedSymbolProfile Valid(SymbolProfile profile)
        => new(profile, SymbolProfileResolutionStatus.Valid, string.Empty);

    public static ResolvedSymbolProfile Invalid(SymbolProfile profile, string message)
        => new(profile, SymbolProfileResolutionStatus.Invalid, message);

    public static ResolvedSymbolProfile Indeterminate(SymbolProfile profile, string message)
        => new(profile, SymbolProfileResolutionStatus.Indeterminate, message);
}

public enum SymbolProfileResolutionStatus
{
    Valid,
    Invalid,
    Indeterminate
}
