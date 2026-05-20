using System.Text.Json;
using PortfolioSaver.Config.Services;
using PortfolioSaver.Core.Constants;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Screensaver.Services;
using Xunit;

namespace PortfolioSaver.Tests.Services;

[Collection("EnvironmentSerial")]
public sealed class SettingsFileServiceTests
{
    private static void DeleteDirectoryWithRetry(string path)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            if (!Directory.Exists(path))
                return;

            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 9)
            {
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException) when (attempt < 9)
            {
                Thread.Sleep(100);
            }
        }
    }

    [Fact]
    public void Save_StripsSecretsFromPersistedSettingsFile()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "PortfolioSaverTests", Guid.NewGuid().ToString("N"));
        string? previousRoot = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT");
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", tempRoot);

        try
        {
            SettingsFileService service = new();
            AppSettings settings = Defaults.CreateSettings();
            settings.DeepSeekApiKey = "live-deepseek-key";

            service.Save(settings);

            string json = File.ReadAllText(service.SettingsPath);
            AppSettings persisted = JsonSerializer.Deserialize<AppSettings>(json)!;

            Assert.True(string.IsNullOrWhiteSpace(persisted.DeepSeekApiKey));
            Assert.DoesNotContain("live-deepseek-key", json, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", previousRoot);
            if (Directory.Exists(tempRoot))
                DeleteDirectoryWithRetry(tempRoot);
        }
    }

    [Fact]
    public void Load_WhenSettingsFileMissing_DoesNotSeedPlaintextSecretPlaceholders()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "PortfolioSaverTests", Guid.NewGuid().ToString("N"));
        string? previousRoot = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT");
        string? previousDeepSeek = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
        string? previousPortfolioSaverDeepSeek = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_DEEPSEEK_API_KEY");
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", tempRoot);
        Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", null);
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_DEEPSEEK_API_KEY", null);

        try
        {
            SettingsFileService service = new();

            AppSettings settings = service.Load();

            Assert.True(string.IsNullOrWhiteSpace(settings.DeepSeekApiKey));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", previousRoot);
            Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", previousDeepSeek);
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_DEEPSEEK_API_KEY", previousPortfolioSaverDeepSeek);
            if (Directory.Exists(tempRoot))
                DeleteDirectoryWithRetry(tempRoot);
        }
    }

    [Fact]
    public void Save_PersistsDeepSeekSecretSecurely_AndRuntimeLoadRestoresIt()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "PortfolioSaverTests", Guid.NewGuid().ToString("N"));
        string? previousRoot = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT");
        string? previousDeepSeek = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
        string? previousPortfolioSaverDeepSeek = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_DEEPSEEK_API_KEY");
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", tempRoot);
        Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", null);
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_DEEPSEEK_API_KEY", null);

        try
        {
            SettingsFileService service = new();
            AppSettings settings = Defaults.CreateSettings();
            settings.DeepSeekApiKey = "live-deepseek-key";

            service.Save(settings);

            string settingsJson = File.ReadAllText(service.SettingsPath);
            string secretsPath = Path.Combine(tempRoot, "provider-secrets.json");
            string secretsJson = File.ReadAllText(secretsPath);

            Assert.DoesNotContain("live-deepseek-key", settingsJson, StringComparison.Ordinal);
            Assert.DoesNotContain("live-deepseek-key", secretsJson, StringComparison.Ordinal);

            AppSettings configLoaded = service.Load();
            ScreensaverSettingsService runtimeService = new();
            AppSettings runtimeLoaded = runtimeService.Load();

            Assert.Equal("live-deepseek-key", configLoaded.DeepSeekApiKey);

            Assert.Equal("live-deepseek-key", runtimeLoaded.DeepSeekApiKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", previousRoot);
            Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", previousDeepSeek);
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_DEEPSEEK_API_KEY", previousPortfolioSaverDeepSeek);
            if (Directory.Exists(tempRoot))
                DeleteDirectoryWithRetry(tempRoot);
        }
    }
}
