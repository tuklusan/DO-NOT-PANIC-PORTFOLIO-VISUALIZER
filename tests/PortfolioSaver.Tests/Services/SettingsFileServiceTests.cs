using System.Text.Json;
using PortfolioSaver.Config.Services;
using PortfolioSaver.Core.Constants;
using PortfolioSaver.Core.Models;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class SettingsFileServiceTests
{
    [Fact]
    public void Save_StripsApiKeysFromPersistedSettingsFile()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "PortfolioSaverTests", Guid.NewGuid().ToString("N"));
        string? previousRoot = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT");
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", tempRoot);

        try
        {
            SettingsFileService service = new();
            AppSettings settings = Defaults.CreateSettings();
            settings.FinnhubApiKey = "live-finnhub-key";
            settings.TwelveDataApiKey = "live-twelvedata-key";
            settings.TiingoApiKey = "live-tiingo-key";
            settings.FinancialModelingPrepApiKey = "live-fmp-key";
            settings.EodhdApiKey = "live-eodhd-key";

            service.Save(settings);

            string json = File.ReadAllText(service.SettingsPath);
            AppSettings persisted = JsonSerializer.Deserialize<AppSettings>(json)!;

            Assert.True(string.IsNullOrWhiteSpace(persisted.FinnhubApiKey));
            Assert.True(string.IsNullOrWhiteSpace(persisted.TwelveDataApiKey));
            Assert.True(string.IsNullOrWhiteSpace(persisted.TiingoApiKey));
            Assert.True(string.IsNullOrWhiteSpace(persisted.FinancialModelingPrepApiKey));
            Assert.True(string.IsNullOrWhiteSpace(persisted.EodhdApiKey));
            Assert.DoesNotContain("live-finnhub-key", json, StringComparison.Ordinal);
            Assert.DoesNotContain("live-twelvedata-key", json, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", previousRoot);
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void Load_WhenSettingsFileMissing_DoesNotSeedPlaintextApiKeyPlaceholders()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "PortfolioSaverTests", Guid.NewGuid().ToString("N"));
        string? previousRoot = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT");
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", tempRoot);

        try
        {
            SettingsFileService service = new();

            AppSettings settings = service.Load();

            Assert.True(string.IsNullOrWhiteSpace(settings.FinnhubApiKey));
            Assert.True(string.IsNullOrWhiteSpace(settings.TwelveDataApiKey));
            Assert.True(string.IsNullOrWhiteSpace(settings.TiingoApiKey));
            Assert.True(string.IsNullOrWhiteSpace(settings.FinancialModelingPrepApiKey));
            Assert.True(string.IsNullOrWhiteSpace(settings.EodhdApiKey));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", previousRoot);
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }
}
