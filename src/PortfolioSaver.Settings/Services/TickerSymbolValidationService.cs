using System.IO;
using System.Net.Http;
using System.Net.NetworkInformation;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Core.Services;
using PortfolioSaver.Data.Services;
using PortfolioSaver.Shared.Helpers;

namespace PortfolioSaver.Config.Services;

public sealed class TickerSymbolValidationService
{
    private readonly SymbolProfileStore _symbolProfileStore =
        new(Path.Combine(PathHelper.GetLocalDataDirectory(), "symbol-profiles.json"));

    public bool IsNetworkAvailable() => NetworkInterface.GetIsNetworkAvailable();

    public async Task<TickerSymbolValidationResult> ValidateAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        TickerSymbolValidationResult result = new()
        {
            NetworkAvailable = IsNetworkAvailable()
        };

        Dictionary<string, SymbolProfile> cachedProfiles = _symbolProfileStore.Load()
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

        if (!result.NetworkAvailable)
        {
            foreach (string symbol in GetSymbolsToValidate(settings))
            {
                string normalizedSymbol = SymbolProfileHeuristics.Normalize(symbol);
                if (cachedProfiles.TryGetValue(normalizedSymbol, out SymbolProfile? cachedProfile))
                    result.Profiles[normalizedSymbol] = cachedProfile;
                else
                    result.Warnings.Add($"No network connection was detected, so '{normalizedSymbol}' could not be validated.");
            }

            return result;
        }

        using HttpClient httpClient = HttpClientFactory.Create(TimeSpan.FromSeconds(Math.Max(3, settings.HttpTimeoutSeconds)));
        SymbolProfileResolverService resolver = new(httpClient);
        if (!settings.DataSources.Any(policy => policy.EnableSingleTickerQueries || policy.EnableBatchTickerQueries))
        {
            result.Warnings.Add("No enabled data source is available for ticker validation.");
            return result;
        }

        foreach (string symbol in GetSymbolsToValidate(settings))
        {
            ResolvedSymbolProfile outcome = await resolver.ResolveAsync(symbol, settings, cancellationToken);
            string normalizedSymbol = SymbolProfileHeuristics.Normalize(symbol);
            result.Profiles[normalizedSymbol] = outcome.Profile;

            switch (outcome.Status)
            {
                case SymbolProfileResolutionStatus.Valid:
                    continue;
                case SymbolProfileResolutionStatus.Invalid:
                    result.InvalidSymbols.Add(normalizedSymbol);
                    break;
                case SymbolProfileResolutionStatus.Indeterminate:
                    result.Warnings.Add(outcome.Message);
                    break;
            }
        }

        foreach ((string symbol, SymbolProfile profile) in result.Profiles)
            cachedProfiles[symbol] = profile;

        _symbolProfileStore.Save(cachedProfiles.Values);
        return result;
    }

    private static IEnumerable<string> GetSymbolsToValidate(AppSettings settings)
    {
        return settings.Groups
            .Where(group => group.Enabled)
            .SelectMany(group => group.Tickers)
            .Where(ticker => ticker.Enabled && !string.IsNullOrWhiteSpace(ticker.Symbol))
            .Select(ticker => ticker.Symbol.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

}

public sealed class TickerSymbolValidationResult
{
    public bool NetworkAvailable { get; set; }
    public List<string> InvalidSymbols { get; } = [];
    public List<string> Warnings { get; } = [];
    public Dictionary<string, SymbolProfile> Profiles { get; } = new(StringComparer.OrdinalIgnoreCase);
}
