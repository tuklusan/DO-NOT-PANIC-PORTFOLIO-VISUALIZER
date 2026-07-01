// ============================================================================
// Copyright (c) 2026 Supratim Sanyal of SANYALnet Labs.
// Proprietary rights reserved except as expressly licensed herein.
//
// DO NOT PANIC PORTFOLIO VISUALIZER
// This file is governed by the SANYALnet Labs Non-Commercial License in the
// root LICENSE file. Non-Commercial use is permitted; Commercial Use and use
// for AI/ML model training are prohibited unless separately authorized.
//
// Attribution is required: "Based on original work by Supratim Sanyal of
// SANYALnet Labs." See LICENSE for full terms, warranty disclaimer, termination,
// patent, trademark, and governing-law provisions.
// ============================================================================
using Xunit;
using System.Reflection;
using PortfolioSaver.Core.Constants;
using PortfolioSaver.Core.Enums;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Core.Services;

namespace PortfolioSaver.Tests.Services;

[Collection("EnvironmentSerial")]
public sealed class AppSettingsNormalizerTests
{
    [Fact]
    public void Clone_CopiesAllWritableSettingsGraphPropertiesAndDeepCopiesLists()
    {
        AppSettings settings = new();
        SetDistinctWritableProperties(settings, nameof(AppSettings.Groups));
        TickerGroup group = new();
        SetDistinctWritableProperties(group, nameof(TickerGroup.Tickers));
        TickerItem ticker = new();
        SetDistinctWritableProperties(ticker);
        group.Tickers = [ticker];
        settings.Groups = [group];

        AppSettings clone = settings.Clone();

        Assert.NotSame(settings, clone);
        AssertEquivalentWritableProperties(settings, clone, nameof(AppSettings.Groups));
        Assert.NotSame(settings.Groups, clone.Groups);
        TickerGroup clonedGroup = Assert.Single(clone.Groups);
        Assert.NotSame(group, clonedGroup);
        AssertEquivalentWritableProperties(group, clonedGroup, nameof(TickerGroup.Tickers));
        Assert.NotSame(group.Tickers, clonedGroup.Tickers);
        TickerItem clonedTicker = Assert.Single(clonedGroup.Tickers);
        Assert.NotSame(ticker, clonedTicker);
        AssertEquivalentWritableProperties(ticker, clonedTicker);
    }

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
    public void Normalize_AiApiKey_PreservesExplicitConfiguredValue()
    {
        AppSettings settings = Defaults.CreateSettings();
        settings.AiApiKey = "test-persisted-ai-key";

        AppSettings normalized = AppSettingsNormalizer.Normalize(settings);

        Assert.Equal("test-persisted-ai-key", normalized.AiApiKey);
    }

    [Fact]
    public void Normalize_AiApiKey_ClearsPlaceholder()
    {
        AppSettings settings = Defaults.CreateSettings();
        settings.AiApiKey = "REPLACE_WITH_AI_API_KEY";

        AppSettings normalized = AppSettingsNormalizer.Normalize(settings);

        Assert.True(string.IsNullOrWhiteSpace(normalized.AiApiKey));
    }

    [Fact]
    public void Normalize_AiApiKey_IgnoresEnvironmentVariableWhenConfigKeyBlank()
    {
        const string environmentName = "OPENROUTER_AI_API_KEY";
        string? previous = Environment.GetEnvironmentVariable(environmentName);
        Environment.SetEnvironmentVariable(environmentName, "env-key");

        try
        {
            AppSettings settings = Defaults.CreateSettings();
            settings.AiApiKey = string.Empty;

            AppSettings normalized = AppSettingsNormalizer.Normalize(settings);

            Assert.True(string.IsNullOrWhiteSpace(normalized.AiApiKey));
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentName, previous);
        }
    }

    [Fact]
    public void Normalize_RetiresLegacyRefreshPair_ToDesktopDefaults()
    {
        AppSettings legacy = Defaults.CreateSettings();
        legacy.RefreshSecondsPortfolio = Defaults.LegacySteadyStateRefreshSeconds;
        legacy.RefreshSecondsOffHours = Defaults.LegacySteadyStateRefreshSeconds;

        AppSettings normalized = AppSettingsNormalizer.Normalize(legacy);

        Assert.Equal(Defaults.DefaultDesktopRefreshSeconds, normalized.RefreshSecondsPortfolio);
        Assert.Equal(Defaults.DefaultDesktopRefreshSeconds, normalized.RefreshSecondsOffHours);
    }

    [Fact]
    public void Normalize_RetiresIntentionalCustomRefreshValues()
    {
        AppSettings custom = Defaults.CreateSettings();
        custom.RefreshSecondsPortfolio = 600;
        custom.RefreshSecondsOffHours = 900;

        AppSettings normalized = AppSettingsNormalizer.Normalize(custom);

        Assert.Equal(Defaults.DefaultDesktopRefreshSeconds, normalized.RefreshSecondsPortfolio);
        Assert.Equal(Defaults.DefaultDesktopRefreshSeconds, normalized.RefreshSecondsOffHours);
    }

    [Fact]
    public void Normalize_RetiresRemoteBackgroundPaths_ToLocalOnlyDefaults()
    {
        AppSettings settings = Defaults.CreateSettings();
        settings.BackgroundImageFolder = "https://example.invalid/backgrounds";
        settings.CustomBackgroundImageFolder = "https://example.invalid/custom-backgrounds";

        AppSettings normalized = AppSettingsNormalizer.Normalize(settings);

        Assert.Equal(Defaults.GetManagedBackgroundCacheFolder(), normalized.BackgroundImageFolder);
        Assert.Equal(string.Empty, normalized.CustomBackgroundImageFolder);
    }

    [Fact]
    public void Normalize_DefaultsNewsScrollerModeToRssFeed()
    {
        AppSettings settings = Defaults.CreateSettings();
        settings.NewsScrollerMode = (NewsScrollerMode)999;

        AppSettings normalized = AppSettingsNormalizer.Normalize(settings);

        Assert.Equal(NewsScrollerMode.RssFeed, normalized.NewsScrollerMode);
    }

    [Fact]
    public void Normalize_DefaultsAiWritingStyleToDouglasAdams()
    {
        AppSettings settings = Defaults.CreateSettings();
        settings.AiWritingStyle = (AiWritingStyle)999;

        AppSettings normalized = AppSettingsNormalizer.Normalize(settings);

        Assert.Equal(AiWritingStyle.DouglasAdams, normalized.AiWritingStyle);
    }

    [Fact]
    public void Normalize_DefaultsAiEndpointAndModelWhenBlank()
    {
        AppSettings settings = Defaults.CreateSettings();
        settings.AiEndpointUrl = "   ";
        settings.AiModelId = " ";

        AppSettings normalized = AppSettingsNormalizer.Normalize(settings);

        Assert.Equal(Defaults.DefaultAiEndpointUrl, normalized.AiEndpointUrl);
        Assert.Equal(Defaults.DefaultAiModelId, normalized.AiModelId);
    }

    [Fact]
    public void Normalize_PreservesConfiguredAiEndpointAndModel()
    {
        AppSettings settings = Defaults.CreateSettings();
        settings.AiEndpointUrl = "https://ai.example.test/v1/chat/completions";
        settings.AiModelId = "example-model";

        AppSettings normalized = AppSettingsNormalizer.Normalize(settings);

        Assert.Equal("https://ai.example.test/v1", normalized.AiEndpointUrl);
        Assert.Equal("example-model", normalized.AiModelId);
    }

    [Fact]
    public void Normalize_MigratesLegacyDefaultAiEndpointToCurrentDefault()
    {
        AppSettings settings = Defaults.CreateSettings();
        settings.AiEndpointUrl = "https://api.deepseek.com/chat/completions";

        AppSettings normalized = AppSettingsNormalizer.Normalize(settings);

        Assert.Equal(Defaults.DefaultAiEndpointUrl, normalized.AiEndpointUrl);
    }

    [Fact]
    public void Normalize_MigratesLegacyDefaultAiModelToCurrentDefault()
    {
        AppSettings settings = Defaults.CreateSettings();
        settings.AiModelId = "deepseek-v4-flash";

        AppSettings normalized = AppSettingsNormalizer.Normalize(settings);

        Assert.Equal(Defaults.DefaultAiModelId, normalized.AiModelId);
    }

    [Fact]
    public void Normalize_MigratesLegacyFiveMinuteNewsRefreshToCurrentMinimum()
    {
        AppSettings settings = Defaults.CreateSettings();
        settings.NewsRefreshMinutes = 5;

        AppSettings normalized = AppSettingsNormalizer.Normalize(settings);

        Assert.Equal(Defaults.MinNewsRefreshMinutes, normalized.NewsRefreshMinutes);
    }

    [Fact]
    public void Normalize_CanonicalizesAiChatCompletionsEndpointToBaseUrl()
    {
        AppSettings settings = Defaults.CreateSettings();
        settings.AiEndpointUrl = "https://localhost:11434/v1/chat/completions/";

        AppSettings normalized = AppSettingsNormalizer.Normalize(settings);

        Assert.Equal("https://localhost:11434/v1", normalized.AiEndpointUrl);
    }

    private static void SetDistinctWritableProperties(object target, params string[] excludedPropertyNames)
    {
        HashSet<string> excluded = excludedPropertyNames.ToHashSet(StringComparer.Ordinal);
        PropertyInfo[] properties = target.GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanRead && property.CanWrite && !excluded.Contains(property.Name))
            .ToArray();

        for (int index = 0; index < properties.Length; index++)
        {
            PropertyInfo property = properties[index];
            Type propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            object value;
            if (propertyType == typeof(string))
            {
                value = $"clone-test-{target.GetType().Name}-{property.Name}";
            }
            else if (propertyType == typeof(int))
            {
                value = 1000 + index;
            }
            else if (propertyType == typeof(double))
            {
                value = 1000.25d + index;
            }
            else if (propertyType == typeof(decimal))
            {
                value = 1000.50m + index;
            }
            else if (propertyType == typeof(bool))
            {
                value = !(bool)(property.GetValue(target) ?? false);
            }
            else if (propertyType.IsEnum)
            {
                Array values = Enum.GetValues(propertyType);
                object current = property.GetValue(target)!;
                value = values.Cast<object>().First(candidate => !Equals(candidate, current));
            }
            else
            {
                throw new NotSupportedException($"Clone coverage test does not know how to assign {target.GetType().Name}.{property.Name} ({property.PropertyType}).");
            }

            property.SetValue(target, value);
        }
    }

    private static void AssertEquivalentWritableProperties(object expected, object actual, params string[] excludedPropertyNames)
    {
        HashSet<string> excluded = excludedPropertyNames.ToHashSet(StringComparer.Ordinal);
        foreach (PropertyInfo property in expected.GetType()
                     .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                     .Where(property => property.CanRead && property.CanWrite && !excluded.Contains(property.Name)))
        {
            Assert.Equal(property.GetValue(expected), property.GetValue(actual));
        }
    }
}
