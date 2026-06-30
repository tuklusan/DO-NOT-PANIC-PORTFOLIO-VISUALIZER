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
using PortfolioSaver.Core.Constants;
using PortfolioSaver.Core.Enums;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Core.Services;
using PortfolioSaver.Core.Validation;
using PortfolioSaver.Shared.Helpers;
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
        Assert.True(string.IsNullOrWhiteSpace(settings.DeepSeekApiKey));
        Assert.False(string.IsNullOrWhiteSpace(settings.BackgroundImageFolder));
        Assert.StartsWith(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            settings.HistoricalCacheRootFolder,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(PathHelper.AppLocalDataFolderName, settings.HistoricalCacheRootFolder, StringComparison.Ordinal);
        Assert.Contains(PathHelper.AppLocalDataFolderName, settings.BackgroundImageFolder, StringComparison.Ordinal);
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

    [Fact]
    public void Validate_SummarizedNewsMode_DoesNotRequireValidRssUrl()
    {
        AppSettings settings = Defaults.CreateSettings();
        settings.NewsScrollerMode = NewsScrollerMode.SummarizedFinancialNews;
        settings.NewsFeedUrl = "not a url";

        SettingsValidator validator = new();
        IReadOnlyList<string> errors = validator.Validate(settings);

        Assert.DoesNotContain(errors, error => error.Contains("News feed URL", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_RssFeedMode_StillRequiresValidRssUrl()
    {
        AppSettings settings = Defaults.CreateSettings();
        settings.NewsScrollerMode = NewsScrollerMode.RssFeed;
        settings.NewsFeedUrl = "not a url";

        SettingsValidator validator = new();
        IReadOnlyList<string> errors = validator.Validate(settings);

        Assert.Contains(errors, error => error.Contains("News feed URL", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_NewsRefreshBelowTenMinutes_ReturnsError()
    {
        AppSettings settings = Defaults.CreateSettings();
        settings.NewsRefreshMinutes = 9;

        SettingsValidator validator = new();
        IReadOnlyList<string> errors = validator.Validate(settings);

        Assert.Contains(errors, error => error.Contains("between 10 minutes and 4 hours", StringComparison.OrdinalIgnoreCase));
    }
}
