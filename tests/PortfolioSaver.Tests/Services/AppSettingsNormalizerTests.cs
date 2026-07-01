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
    public void Normalize_AiApiKey_PrefersExplicitConfiguredValue_OverOpenRouterEnvironmentVariable()
    {
        const string environmentName = "OPENROUTER_AI_API_KEY";
        string? previous = Environment.GetEnvironmentVariable(environmentName);
        Environment.SetEnvironmentVariable(environmentName, "test-env-openrouter-key");

        try
        {
            AppSettings settings = Defaults.CreateSettings();
            settings.DeepSeekApiKey = "test-persisted-deepseek-key";

            AppSettings normalized = AppSettingsNormalizer.Normalize(settings);

            Assert.Equal("test-persisted-deepseek-key", normalized.DeepSeekApiKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentName, previous);
        }
    }

    [Fact]
    public void Normalize_AiApiKey_WhenNoEnvironmentVariablesSet_ClearsPlaceholder()
    {
        Dictionary<string, string?> previous = CaptureEnvironmentVariables(Defaults.AiApiKeyEnvironmentVariableNames);
        ClearEnvironmentVariables(Defaults.AiApiKeyEnvironmentVariableNames);

        try
        {
            AppSettings settings = Defaults.CreateSettings();
            settings.DeepSeekApiKey = "REPLACE_WITH_DEEPSEEK_API_KEY";

            AppSettings normalized = AppSettingsNormalizer.Normalize(settings);

            Assert.True(string.IsNullOrWhiteSpace(normalized.DeepSeekApiKey));
        }
        finally
        {
            RestoreEnvironmentVariables(previous);
        }
    }

    [Fact]
    public void Normalize_AiApiKey_PrefersEnvironmentVariable_OverPlaceholder()
    {
        Dictionary<string, string?> previous = CaptureEnvironmentVariables(Defaults.AiApiKeyEnvironmentVariableNames);
        ClearEnvironmentVariables(Defaults.AiApiKeyEnvironmentVariableNames);

        try
        {
            Environment.SetEnvironmentVariable(Defaults.AiApiKeyEnvironmentVariableNames[0], "env-key");
            AppSettings settings = Defaults.CreateSettings();
            settings.DeepSeekApiKey = "REPLACE_WITH_DEEPSEEK_API_KEY";

            AppSettings normalized = AppSettingsNormalizer.Normalize(settings);

            Assert.Equal("env-key", normalized.DeepSeekApiKey);
        }
        finally
        {
            RestoreEnvironmentVariables(previous);
        }
    }

    [Fact]
    public void Normalize_AiApiKey_UsesDocumentedEnvironmentVariablePrecedence_WhenExplicitKeyMissing()
    {
        IReadOnlyList<string> names = Defaults.AiApiKeyEnvironmentVariableNames;
        Dictionary<string, string?> previous = CaptureEnvironmentVariables(names);

        try
        {
            for (int index = 0; index < names.Count; index++)
                Environment.SetEnvironmentVariable(names[index], $"key-{index}");

            AppSettings settings = Defaults.CreateSettings();
            settings.DeepSeekApiKey = string.Empty;

            AppSettings normalized = AppSettingsNormalizer.Normalize(settings);

            Assert.Equal("key-0", normalized.DeepSeekApiKey);
        }
        finally
        {
            RestoreEnvironmentVariables(previous);
        }
    }

    private static Dictionary<string, string?> CaptureEnvironmentVariables(IEnumerable<string> names)
        => names.ToDictionary(
            static name => name,
            static name => Environment.GetEnvironmentVariable(name),
            StringComparer.Ordinal);

    private static void ClearEnvironmentVariables(IEnumerable<string> names)
    {
        foreach (string name in names)
            Environment.SetEnvironmentVariable(name, null);
    }

    private static void RestoreEnvironmentVariables(IReadOnlyDictionary<string, string?> previous)
    {
        foreach ((string name, string? value) in previous)
            Environment.SetEnvironmentVariable(name, value);
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

    [Fact]
    public void Normalize_DefaultsDeepSeekEndpointAndModelWhenBlank()
    {
        AppSettings settings = Defaults.CreateSettings();
        settings.DeepSeekEndpointUrl = "   ";
        settings.DeepSeekModelId = " ";

        AppSettings normalized = AppSettingsNormalizer.Normalize(settings);

        Assert.Equal(Defaults.DefaultDeepSeekEndpointUrl, normalized.DeepSeekEndpointUrl);
        Assert.Equal(Defaults.DefaultDeepSeekModelId, normalized.DeepSeekModelId);
    }

    [Fact]
    public void Normalize_MigratesLegacyDeepSeekDefaultsToOpenRouterDefaults()
    {
        AppSettings settings = Defaults.CreateSettings();
        settings.DeepSeekEndpointUrl = "https://api.deepseek.com/chat/completions";
        settings.DeepSeekModelId = "deepseek-v4-flash";

        AppSettings normalized = AppSettingsNormalizer.Normalize(settings);

        Assert.Equal(Defaults.DefaultDeepSeekEndpointUrl, normalized.DeepSeekEndpointUrl);
        Assert.Equal(Defaults.DefaultDeepSeekModelId, normalized.DeepSeekModelId);
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
    public void Normalize_CanonicalizesDeepSeekChatCompletionsEndpointToBaseUrl()
    {
        AppSettings settings = Defaults.CreateSettings();
        settings.DeepSeekEndpointUrl = "https://localhost:11434/v1/chat/completions/";

        AppSettings normalized = AppSettingsNormalizer.Normalize(settings);

        Assert.Equal("https://localhost:11434/v1", normalized.DeepSeekEndpointUrl);
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
