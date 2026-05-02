using System.IO;
using System.Text.Json;
using PortfolioSaver.Core.Constants;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Core.Services;
using PortfolioSaver.Data.Services;
using PortfolioSaver.Shared.Helpers;

namespace PortfolioSaver.Config.Services;

public sealed class SettingsFileService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ProviderSecretStoreService _providerSecretStoreService = new();

    public string SettingsPath => Path.Combine(PathHelper.GetAppDataDirectory(), "settings.json");

    public AppSettings Load()
    {
        AppSettings settings = Defaults.CreateSettings();
        if (File.Exists(SettingsPath))
        {
            string json = File.ReadAllText(SettingsPath);
            settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? settings;
        }

        _providerSecretStoreService.OverlaySecrets(settings);
        return AppSettingsNormalizer.Normalize(settings);
    }

    public void Save(AppSettings settings)
    {
        _providerSecretStoreService.Save(settings);

        AppSettings persisted = CreateSanitizedCopy(settings);
        string json = JsonSerializer.Serialize(persisted, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
    }

    private static AppSettings CreateSanitizedCopy(AppSettings settings)
    {
        AppSettings copy = JsonSerializer.Deserialize<AppSettings>(
            JsonSerializer.Serialize(settings),
            JsonOptions) ?? Defaults.CreateSettings();

        copy.FinnhubApiKey = string.Empty;
        copy.TwelveDataApiKey = string.Empty;
        copy.TiingoApiKey = string.Empty;
        copy.FinancialModelingPrepApiKey = string.Empty;
        copy.EodhdApiKey = string.Empty;

        return copy;
    }
}
