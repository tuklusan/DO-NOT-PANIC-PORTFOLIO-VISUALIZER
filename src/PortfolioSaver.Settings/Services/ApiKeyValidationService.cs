using PortfolioSaver.Core.Models;

namespace PortfolioSaver.Config.Services;

public sealed class ApiKeyValidationService
{
    private static readonly (string Provider, Func<AppSettings, string> Getter)[] LegacyProviders =
    [
        ("Finnhub", static settings => settings.FinnhubApiKey),
        ("Twelve Data", static settings => settings.TwelveDataApiKey),
        ("Tiingo", static settings => settings.TiingoApiKey),
        ("Financial Modeling Prep", static settings => settings.FinancialModelingPrepApiKey),
        ("EODHD", static settings => settings.EodhdApiKey)
    ];

    public Task<ApiKeyValidationResult> ValidateAsync(AppSettings settings, CancellationToken cancellationToken = default)
        => ValidateAsync(settings, progress: null, cancellationToken);

    public Task<ApiKeyValidationResult> ValidateAsync(
        AppSettings settings,
        IProgress<ApiKeyValidationProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ApiKeyValidationResult result = new();
        foreach ((string provider, Func<AppSettings, string> getter) in LegacyProviders)
        {
            if (!string.IsNullOrWhiteSpace(getter(settings)))
            {
                progress?.Report(new ApiKeyValidationProgress(
                    provider,
                    true,
                    "Unused in YFinance.NET-only mode"));
            }
        }

        return Task.FromResult(result);
    }
}

public sealed class ApiKeyValidationResult
{
    public List<string> Errors { get; } = [];
    public bool IsValid => Errors.Count == 0;
}

public sealed record ApiKeyValidationProgress(
    string Provider,
    bool IsValid,
    string Message);
