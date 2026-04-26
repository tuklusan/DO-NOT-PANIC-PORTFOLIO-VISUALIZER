using System.IO;
using System.Text.Json;
using PortfolioSaver.Core.Constants;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Core.Services;
using PortfolioSaver.Shared.Helpers;

namespace PortfolioSaver.Config.Services;

public sealed class SettingsFileService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public string SettingsPath => Path.Combine(PathHelper.GetAppDataDirectory(), "settings.json");

    public AppSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            AppSettings seeded = AppSettingsNormalizer.Normalize(Defaults.CreateSettings());
            SeedConfigOnlyApiKeyPlaceholders(seeded);
            return seeded;
        }

        string json = File.ReadAllText(SettingsPath);
        return AppSettingsNormalizer.Normalize(JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? Defaults.CreateSettings());
    }

    public void Save(AppSettings settings)
    {
        string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
    }

    private static void SeedConfigOnlyApiKeyPlaceholders(AppSettings settings)
    {
        settings.FinnhubApiKey = "abcdefghijklmnopqrstuvwxyz01234567890abc";
        settings.TwelveDataApiKey = "abcdefghijklmnopqrstuvwxyz012345";
        settings.TiingoApiKey = "abcdefghijklmnopqrstuvwxyz01234567890abc";
        settings.FinancialModelingPrepApiKey = "abcdefghijklmnopqrstuvwxyz012345";
        settings.EodhdApiKey = "abcdefghijklmn.01234567";
    }
}
