using PortfolioSaver.Config.Services;
using PortfolioSaver.Core.Constants;
using PortfolioSaver.Core.Models;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class ApiKeyValidationServiceTests
{
    [Fact]
    public async Task ValidateAsync_EmptyLegacyKeys_DoNotBlockValidation()
    {
        ApiKeyValidationService service = new();

        ApiKeyValidationResult result = await service.ValidateAsync(Defaults.CreateSettings());

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ValidateAsync_LegacyKeysAreReportedAsUnusedInsteadOfRequired()
    {
        ApiKeyValidationService service = new();
        AppSettings settings = Defaults.CreateSettings();
        settings.FinnhubApiKey = "finnhub-key";
        settings.TwelveDataApiKey = "twelve-key";
        settings.TiingoApiKey = "tiingo-key";
        settings.FinancialModelingPrepApiKey = "fmp-key";
        settings.EodhdApiKey = "eodhd-key";

        List<ApiKeyValidationProgress> progressEntries = [];
        ApiKeyValidationResult result = await service.ValidateAsync(
            settings,
            new Progress<ApiKeyValidationProgress>(entry => progressEntries.Add(entry)));

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Equal(5, progressEntries.Count);
        Assert.All(progressEntries, entry =>
        {
            Assert.True(entry.IsValid);
            Assert.Equal("Unused in YFinance.NET-only mode", entry.Message);
        });
    }
}
