using PortfolioSaver.Core.Constants;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Core.Services;
using PortfolioSaver.Core.Validation;
using Xunit;

namespace PortfolioSaver.Tests.Validation;

public sealed class SettingsValidatorTests
{
    [Fact]
    public void Validate_DefaultSettings_ReturnsNoErrors()
    {
        var settings = Defaults.CreateSettings();
        SettingsValidator validator = new();
        var errors = validator.Validate(settings);
        Assert.Empty(errors);
        Assert.True(string.IsNullOrWhiteSpace(settings.FinnhubApiKey));
        Assert.True(string.IsNullOrWhiteSpace(settings.TwelveDataApiKey));
        Assert.False(string.IsNullOrWhiteSpace(settings.BackgroundImageFolder));
        Assert.StartsWith(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            settings.HistoricalCacheRootFolder,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Normalize_LegacyTempHistoryCache_MigratesToManagedCacheFolder()
    {
        AppSettings settings = new()
        {
            HistoricalCacheRootFolder = Defaults.GetLegacyHistoricalCacheFolder()
        };

        AppSettings normalized = AppSettingsNormalizer.Normalize(settings);
        Assert.Equal(Defaults.GetHistoricalCacheFolder(), normalized.HistoricalCacheRootFolder);
    }
}
