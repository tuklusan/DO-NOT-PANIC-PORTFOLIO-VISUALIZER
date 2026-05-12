using System.Text.Json;
using PortfolioSaver.Config.Services;
using PortfolioSaver.Core.Constants;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Screensaver.Services;
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
            settings.DeepSeekApiKey = "live-deepseek-key";

            service.Save(settings);

            string json = File.ReadAllText(service.SettingsPath);
            AppSettings persisted = JsonSerializer.Deserialize<AppSettings>(json)!;

            Assert.True(string.IsNullOrWhiteSpace(persisted.FinnhubApiKey));
            Assert.True(string.IsNullOrWhiteSpace(persisted.TwelveDataApiKey));
            Assert.True(string.IsNullOrWhiteSpace(persisted.TiingoApiKey));
            Assert.True(string.IsNullOrWhiteSpace(persisted.FinancialModelingPrepApiKey));
            Assert.True(string.IsNullOrWhiteSpace(persisted.EodhdApiKey));
            Assert.True(string.IsNullOrWhiteSpace(persisted.DeepSeekApiKey));
            Assert.DoesNotContain("live-finnhub-key", json, StringComparison.Ordinal);
            Assert.DoesNotContain("live-twelvedata-key", json, StringComparison.Ordinal);
            Assert.DoesNotContain("live-deepseek-key", json, StringComparison.Ordinal);
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
        string? previousFinnhub = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_FINNHUB_API_KEY");
        string? previousTwelve = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_TWELVEDATA_API_KEY");
        string? previousTiingo = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_TIINGO_API_KEY");
        string? previousFmp = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_FMP_API_KEY");
        string? previousEodhd = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_EODHD_API_KEY");
        string? previousDeepSeek = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
        string? previousPortfolioSaverDeepSeek = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_DEEPSEEK_API_KEY");
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", tempRoot);
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_FINNHUB_API_KEY", null);
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_TWELVEDATA_API_KEY", null);
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_TIINGO_API_KEY", null);
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_FMP_API_KEY", null);
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_EODHD_API_KEY", null);
        Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", null);
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_DEEPSEEK_API_KEY", null);

        try
        {
            SettingsFileService service = new();

            AppSettings settings = service.Load();

            Assert.True(string.IsNullOrWhiteSpace(settings.FinnhubApiKey));
            Assert.True(string.IsNullOrWhiteSpace(settings.TwelveDataApiKey));
            Assert.True(string.IsNullOrWhiteSpace(settings.TiingoApiKey));
            Assert.True(string.IsNullOrWhiteSpace(settings.FinancialModelingPrepApiKey));
            Assert.True(string.IsNullOrWhiteSpace(settings.EodhdApiKey));
            Assert.True(string.IsNullOrWhiteSpace(settings.DeepSeekApiKey));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", previousRoot);
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_FINNHUB_API_KEY", previousFinnhub);
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_TWELVEDATA_API_KEY", previousTwelve);
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_TIINGO_API_KEY", previousTiingo);
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_FMP_API_KEY", previousFmp);
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_EODHD_API_KEY", previousEodhd);
            Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", previousDeepSeek);
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_DEEPSEEK_API_KEY", previousPortfolioSaverDeepSeek);
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void Save_PersistsProviderSecretsSecurely_AndRuntimeLoadRestoresThem()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "PortfolioSaverTests", Guid.NewGuid().ToString("N"));
        string? previousRoot = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT");
        string? previousFinnhub = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_FINNHUB_API_KEY");
        string? previousTwelve = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_TWELVEDATA_API_KEY");
        string? previousTiingo = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_TIINGO_API_KEY");
        string? previousFmp = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_FMP_API_KEY");
        string? previousEodhd = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_EODHD_API_KEY");
        string? previousDeepSeek = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
        string? previousPortfolioSaverDeepSeek = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_DEEPSEEK_API_KEY");
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", tempRoot);
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_FINNHUB_API_KEY", null);
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_TWELVEDATA_API_KEY", null);
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_TIINGO_API_KEY", null);
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_FMP_API_KEY", null);
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_EODHD_API_KEY", null);
        Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", null);
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_DEEPSEEK_API_KEY", null);

        try
        {
            SettingsFileService service = new();
            AppSettings settings = Defaults.CreateSettings();
            settings.FinnhubApiKey = "live-finnhub-key";
            settings.TwelveDataApiKey = "live-twelvedata-key";
            settings.TiingoApiKey = "live-tiingo-key";
            settings.FinancialModelingPrepApiKey = "live-fmp-key";
            settings.EodhdApiKey = "live-eodhd-key";
            settings.DeepSeekApiKey = "live-deepseek-key";

            service.Save(settings);

            string settingsJson = File.ReadAllText(service.SettingsPath);
            string secretsPath = Path.Combine(tempRoot, "provider-secrets.json");
            string secretsJson = File.ReadAllText(secretsPath);

            Assert.DoesNotContain("live-finnhub-key", settingsJson, StringComparison.Ordinal);
            Assert.DoesNotContain("live-twelvedata-key", settingsJson, StringComparison.Ordinal);
            Assert.DoesNotContain("live-finnhub-key", secretsJson, StringComparison.Ordinal);
            Assert.DoesNotContain("live-twelvedata-key", secretsJson, StringComparison.Ordinal);
            Assert.DoesNotContain("live-deepseek-key", settingsJson, StringComparison.Ordinal);
            Assert.DoesNotContain("live-deepseek-key", secretsJson, StringComparison.Ordinal);

            AppSettings configLoaded = service.Load();
            ScreensaverSettingsService runtimeService = new();
            AppSettings runtimeLoaded = runtimeService.Load();

            Assert.Equal("live-finnhub-key", configLoaded.FinnhubApiKey);
            Assert.Equal("live-twelvedata-key", configLoaded.TwelveDataApiKey);
            Assert.Equal("live-tiingo-key", configLoaded.TiingoApiKey);
            Assert.Equal("live-fmp-key", configLoaded.FinancialModelingPrepApiKey);
            Assert.Equal("live-eodhd-key", configLoaded.EodhdApiKey);
            Assert.Equal("live-deepseek-key", configLoaded.DeepSeekApiKey);

            Assert.Equal("live-finnhub-key", runtimeLoaded.FinnhubApiKey);
            Assert.Equal("live-twelvedata-key", runtimeLoaded.TwelveDataApiKey);
            Assert.Equal("live-tiingo-key", runtimeLoaded.TiingoApiKey);
            Assert.Equal("live-fmp-key", runtimeLoaded.FinancialModelingPrepApiKey);
            Assert.Equal("live-eodhd-key", runtimeLoaded.EodhdApiKey);
            Assert.Equal("live-deepseek-key", runtimeLoaded.DeepSeekApiKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", previousRoot);
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_FINNHUB_API_KEY", previousFinnhub);
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_TWELVEDATA_API_KEY", previousTwelve);
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_TIINGO_API_KEY", previousTiingo);
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_FMP_API_KEY", previousFmp);
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_EODHD_API_KEY", previousEodhd);
            Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", previousDeepSeek);
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_DEEPSEEK_API_KEY", previousPortfolioSaverDeepSeek);
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }
}
