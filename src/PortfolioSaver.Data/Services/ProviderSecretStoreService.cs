using System.Text.Json;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Data.Interfaces;
using PortfolioSaver.Shared.Helpers;

namespace PortfolioSaver.Data.Services;

public sealed class ProviderSecretStoreService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly ISettingsProtectionService _settingsProtectionService;

    public ProviderSecretStoreService(ISettingsProtectionService? settingsProtectionService = null)
    {
        _settingsProtectionService = settingsProtectionService ?? new SettingsProtectionService();
    }

    public string SecretsPath => Path.Combine(PathHelper.GetAppDataDirectory(), "provider-secrets.json");

    public void OverlaySecrets(AppSettings settings)
    {
        ProviderSecretsDto dto = LoadSecretsDto();
        ApplySecret(settings, dto.FinnhubApiKey, static s => s.FinnhubApiKey, static (s, v) => s.FinnhubApiKey = v);
        ApplySecret(settings, dto.TwelveDataApiKey, static s => s.TwelveDataApiKey, static (s, v) => s.TwelveDataApiKey = v);
        ApplySecret(settings, dto.TiingoApiKey, static s => s.TiingoApiKey, static (s, v) => s.TiingoApiKey = v);
        ApplySecret(settings, dto.FinancialModelingPrepApiKey, static s => s.FinancialModelingPrepApiKey, static (s, v) => s.FinancialModelingPrepApiKey = v);
        ApplySecret(settings, dto.EodhdApiKey, static s => s.EodhdApiKey, static (s, v) => s.EodhdApiKey = v);
        ApplySecret(settings, dto.DeepSeekApiKey, static s => s.DeepSeekApiKey, static (s, v) => s.DeepSeekApiKey = v);
    }

    public void Save(AppSettings settings)
    {
        ProviderSecretsDto dto = LoadSecretsDto();

        dto.FinnhubApiKey = ResolvePersistedProtectedValue(settings.FinnhubApiKey, "PORTFOLIOSAVER_FINNHUB_API_KEY", dto.FinnhubApiKey);
        dto.TwelveDataApiKey = ResolvePersistedProtectedValue(settings.TwelveDataApiKey, "PORTFOLIOSAVER_TWELVEDATA_API_KEY", dto.TwelveDataApiKey);
        dto.TiingoApiKey = ResolvePersistedProtectedValue(settings.TiingoApiKey, "PORTFOLIOSAVER_TIINGO_API_KEY", dto.TiingoApiKey);
        dto.FinancialModelingPrepApiKey = ResolvePersistedProtectedValue(settings.FinancialModelingPrepApiKey, "PORTFOLIOSAVER_FMP_API_KEY", dto.FinancialModelingPrepApiKey);
        dto.EodhdApiKey = ResolvePersistedProtectedValue(settings.EodhdApiKey, "PORTFOLIOSAVER_EODHD_API_KEY", dto.EodhdApiKey);
        dto.DeepSeekApiKey = ResolvePersistedProtectedValue(settings.DeepSeekApiKey, new[] { "DEEPSEEK_API_KEY", "PORTFOLIOSAVER_DEEPSEEK_API_KEY" }, dto.DeepSeekApiKey);

        if (!dto.HasAnySecrets())
        {
            if (File.Exists(SecretsPath))
                File.Delete(SecretsPath);

            return;
        }

        string? directory = Path.GetDirectoryName(SecretsPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        string json = JsonSerializer.Serialize(dto, JsonOptions);
        File.WriteAllText(SecretsPath, json);
    }

    private void ApplySecret(
        AppSettings settings,
        string protectedValue,
        Func<AppSettings, string> getter,
        Action<AppSettings, string> setter)
    {
        if (!string.IsNullOrWhiteSpace(getter(settings)))
            return;

        string unprotected = UnprotectSafe(protectedValue);
        if (!string.IsNullOrWhiteSpace(unprotected))
            setter(settings, unprotected);
    }

    private string ResolvePersistedProtectedValue(string currentValue, string environmentVariableName, string existingProtectedValue)
        => ResolvePersistedProtectedValue(currentValue, new[] { environmentVariableName }, existingProtectedValue);

    private string ResolvePersistedProtectedValue(string currentValue, IEnumerable<string> environmentVariableNames, string existingProtectedValue)
    {
        string trimmed = (currentValue ?? string.Empty).Trim();
        string environmentValue = GetFirstEnvironmentVariableValue(environmentVariableNames);

        if (!string.IsNullOrWhiteSpace(environmentValue) &&
            string.Equals(trimmed, environmentValue, StringComparison.Ordinal))
        {
            return existingProtectedValue ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(trimmed))
            return string.Empty;

        return _settingsProtectionService.Protect(trimmed);
    }

    private static string GetFirstEnvironmentVariableValue(IEnumerable<string> environmentVariableNames)
    {
        foreach (string name in environmentVariableNames)
        {
            string value = (Environment.GetEnvironmentVariable(name) ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return string.Empty;
    }

    private ProviderSecretsDto LoadSecretsDto()
    {
        if (!File.Exists(SecretsPath))
            return new ProviderSecretsDto();

        try
        {
            string json = File.ReadAllText(SecretsPath);
            return JsonSerializer.Deserialize<ProviderSecretsDto>(json, JsonOptions) ?? new ProviderSecretsDto();
        }
        catch
        {
            return new ProviderSecretsDto();
        }
    }

    private string UnprotectSafe(string protectedValue)
    {
        if (string.IsNullOrWhiteSpace(protectedValue))
            return string.Empty;

        try
        {
            return _settingsProtectionService.Unprotect(protectedValue);
        }
        catch
        {
            return string.Empty;
        }
    }

    private sealed class ProviderSecretsDto
    {
        public string FinnhubApiKey { get; set; } = string.Empty;
        public string TwelveDataApiKey { get; set; } = string.Empty;
        public string TiingoApiKey { get; set; } = string.Empty;
        public string FinancialModelingPrepApiKey { get; set; } = string.Empty;
        public string EodhdApiKey { get; set; } = string.Empty;
        public string DeepSeekApiKey { get; set; } = string.Empty;

        public bool HasAnySecrets()
            => !string.IsNullOrWhiteSpace(FinnhubApiKey) ||
               !string.IsNullOrWhiteSpace(TwelveDataApiKey) ||
               !string.IsNullOrWhiteSpace(TiingoApiKey) ||
               !string.IsNullOrWhiteSpace(FinancialModelingPrepApiKey) ||
               !string.IsNullOrWhiteSpace(EodhdApiKey) ||
               !string.IsNullOrWhiteSpace(DeepSeekApiKey);
    }
}
