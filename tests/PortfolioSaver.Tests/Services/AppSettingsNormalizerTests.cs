using Xunit;
using PortfolioSaver.Core.Constants;
using PortfolioSaver.Core.Enums;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Core.Services;

namespace PortfolioSaver.Tests.Services;

public sealed class AppSettingsNormalizerTests
{
    [Fact]
    public void Normalize_ClampsTickersPerTapeToEight()
    {
        AppSettings settings = Defaults.CreateSettings();
        settings.Groups =
        [
            new TickerGroup
            {
                Name = "Tape 1",
                Direction = ScrollDirection.Left,
                Enabled = true,
                Tickers = Enumerable.Range(1, 20)
                    .Select(index => new TickerItem { Symbol = $"T{index:00}", Enabled = true })
                    .ToList()
            }
        ];

        AppSettings normalized = AppSettingsNormalizer.Normalize(settings);

        Assert.Equal(8, normalized.Groups[0].Tickers.Count);
    }

    [Fact]
    public void Normalize_AppliesAlternatingDirectionsForLegacyAllLeftSettings()
    {
        AppSettings settings = Defaults.CreateSettings();
        settings.Groups =
        [
            new TickerGroup { Name = "Tape 1", Direction = ScrollDirection.Left, Enabled = true },
            new TickerGroup { Name = "Tape 2", Direction = ScrollDirection.Left, Enabled = true },
            new TickerGroup { Name = "Tape 3", Direction = ScrollDirection.Left, Enabled = true },
            new TickerGroup { Name = "Tape 4", Direction = ScrollDirection.Left, Enabled = true }
        ];

        AppSettings normalized = AppSettingsNormalizer.Normalize(settings);

        Assert.Equal(ScrollDirection.Left, normalized.Groups[0].Direction);
        Assert.Equal(ScrollDirection.Right, normalized.Groups[1].Direction);
        Assert.Equal(ScrollDirection.Left, normalized.Groups[2].Direction);
        Assert.Equal(ScrollDirection.Right, normalized.Groups[3].Direction);
    }

    [Fact]
    public void Normalize_PreservesExplicitDirectionWhenAnyRightDirectionAlreadyConfigured()
    {
        AppSettings settings = Defaults.CreateSettings();
        settings.Groups =
        [
            new TickerGroup { Name = "Tape 1", Direction = ScrollDirection.Left, Enabled = true },
            new TickerGroup { Name = "Tape 2", Direction = ScrollDirection.Right, Enabled = true },
            new TickerGroup { Name = "Tape 3", Direction = ScrollDirection.Right, Enabled = true }
        ];

        AppSettings normalized = AppSettingsNormalizer.Normalize(settings);

        Assert.Equal(ScrollDirection.Left, normalized.Groups[0].Direction);
        Assert.Equal(ScrollDirection.Right, normalized.Groups[1].Direction);
        Assert.Equal(ScrollDirection.Right, normalized.Groups[2].Direction);
    }

    [Fact]
    public void Normalize_ClampsTapeCountToFour()
    {
        AppSettings settings = Defaults.CreateSettings();
        settings.Groups = Enumerable.Range(1, 10)
            .Select(index => new TickerGroup
            {
                Name = $"Tape {index}",
                Direction = ScrollDirection.Left,
                Enabled = true,
                Tickers = [new TickerItem { Symbol = $"SYM{index}", Enabled = true }]
            })
            .ToList();

        AppSettings normalized = AppSettingsNormalizer.Normalize(settings);

        Assert.Equal(Defaults.MaxTapeCount, normalized.Groups.Count);
    }

    [Fact]
    public void Normalize_AppliesDifferentiatedTapeSpeedsForLegacyUniformBaseline()
    {
        AppSettings settings = Defaults.CreateSettings();
        settings.Groups =
        [
            new TickerGroup { Name = "Tape 1", Direction = ScrollDirection.Left, Enabled = true, Speed = Defaults.DefaultTapeBaseSpeed },
            new TickerGroup { Name = "Tape 2", Direction = ScrollDirection.Right, Enabled = true, Speed = Defaults.DefaultTapeBaseSpeed },
            new TickerGroup { Name = "Tape 3", Direction = ScrollDirection.Left, Enabled = true, Speed = Defaults.DefaultTapeBaseSpeed },
            new TickerGroup { Name = "Tape 4", Direction = ScrollDirection.Right, Enabled = true, Speed = Defaults.DefaultTapeBaseSpeed }
        ];

        AppSettings normalized = AppSettingsNormalizer.Normalize(settings);

        Assert.Equal(Defaults.GetDefaultTapeSpeed(0), normalized.Groups[0].Speed);
        Assert.Equal(Defaults.GetDefaultTapeSpeed(1), normalized.Groups[1].Speed);
        Assert.Equal(Defaults.GetDefaultTapeSpeed(2), normalized.Groups[2].Speed);
        Assert.Equal(Defaults.GetDefaultTapeSpeed(3), normalized.Groups[3].Speed);
    }

    [Fact]
    public void Normalize_WhenGroupsEmpty_RestoresApprovedDefaults()
    {
        AppSettings settings = Defaults.CreateSettings();
        settings.Groups = [];

        AppSettings normalized = AppSettingsNormalizer.Normalize(settings);
        AppSettings defaults = Defaults.CreateSettings();

        Assert.Equal(defaults.Groups.Count, normalized.Groups.Count);
        Assert.Equal(defaults.Groups[0].Name, normalized.Groups[0].Name);
        Assert.Equal(defaults.Groups[0].Tickers[0].Symbol, normalized.Groups[0].Tickers[0].Symbol);
    }

    [Fact]
    public void Normalize_DeepSeekApiKey_PrefersEnvironmentVariable_OverPersistedValue()
    {
        const string environmentName = "DEEPSEEK_API_KEY";
        string? previous = Environment.GetEnvironmentVariable(environmentName);
        Environment.SetEnvironmentVariable(environmentName, "env-deepseek-key");

        try
        {
            AppSettings settings = Defaults.CreateSettings();
            settings.DeepSeekApiKey = "persisted-deepseek-key";

            AppSettings normalized = AppSettingsNormalizer.Normalize(settings);

            Assert.Equal("env-deepseek-key", normalized.DeepSeekApiKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentName, previous);
        }
    }

    [Fact]
    public void Normalize_DeepSeekPlaceholder_DoNotBlockEnvironmentVariableUsage()
    {
        const string environmentName = "DEEPSEEK_API_KEY";
        string? previous = Environment.GetEnvironmentVariable(environmentName);
        Environment.SetEnvironmentVariable(environmentName, null);

        try
        {
            AppSettings settings = Defaults.CreateSettings();
            settings.DeepSeekApiKey = "abcdefghijklmnopqrstuvwxyz012345";

            AppSettings normalized = AppSettingsNormalizer.Normalize(settings);

            Assert.True(string.IsNullOrWhiteSpace(normalized.DeepSeekApiKey));
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentName, previous);
        }
    }

    [Fact]
    public void Normalize_MigratesLegacySteadyStateRefreshPair_ToDesktopDefaults()
    {
        AppSettings legacy = Defaults.CreateSettings();
        legacy.RefreshSecondsPortfolio = Defaults.LegacySteadyStateRefreshSeconds;
        legacy.RefreshSecondsOffHours = Defaults.LegacySteadyStateRefreshSeconds;

        AppSettings normalized = AppSettingsNormalizer.Normalize(legacy);

        Assert.Equal(Defaults.DefaultDesktopRefreshSeconds, normalized.RefreshSecondsPortfolio);
        Assert.Equal(Defaults.DefaultDesktopRefreshSeconds, normalized.RefreshSecondsOffHours);
    }

    [Fact]
    public void Normalize_PreservesIntentionalCustomRefreshValues()
    {
        AppSettings custom = Defaults.CreateSettings();
        custom.RefreshSecondsPortfolio = 600;
        custom.RefreshSecondsOffHours = 900;

        AppSettings normalized = AppSettingsNormalizer.Normalize(custom);

        Assert.Equal(600, normalized.RefreshSecondsPortfolio);
        Assert.Equal(900, normalized.RefreshSecondsOffHours);
    }

    [Fact]
    public void Normalize_DefaultsNewsScrollerModeToSummarizedFinancialNews()
    {
        AppSettings settings = Defaults.CreateSettings();
        settings.NewsScrollerMode = (NewsScrollerMode)999;

        AppSettings normalized = AppSettingsNormalizer.Normalize(settings);

        Assert.Equal(NewsScrollerMode.SummarizedFinancialNews, normalized.NewsScrollerMode);
    }

    [Fact]
    public void Normalize_DefaultsDeepSeekWritingStyleToDouglasAdams()
    {
        AppSettings settings = Defaults.CreateSettings();
        settings.DeepSeekWritingStyle = (DeepSeekWritingStyle)999;

        AppSettings normalized = AppSettingsNormalizer.Normalize(settings);

        Assert.Equal(DeepSeekWritingStyle.DouglasAdams, normalized.DeepSeekWritingStyle);
    }
}
