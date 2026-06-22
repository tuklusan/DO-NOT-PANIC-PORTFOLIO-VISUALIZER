// ============================================================================
// Copyright (c) 2026 Supratim Sanyal of SANYALnet Labs.
// Proprietary rights reserved except as expressly licensed herein.
//
// DO NOT PANIC PORTFOLIO VIEWER
// This software and its derivatives are licensed for STRICTLY NON-COMMERCIAL,
// personal, educational, or hobbyist use only. Commercial exploitation,
// corporate internal operations, or AI model training are strictly forbidden.
//
// ATTRIBUTION & DEPENDENCIES: This application incorporates the YFinance library,
// which is licensed under the Apache License, Version 2.0. A copy of the Apache
// License is provided within the distribution environment.
//
// FINANCIAL DISCLAIMER: This software is a passive visualization tool only.
// It does not provide financial, investment, legal, or tax advice. All data
// calculation and scraping outputs are provided 'AS IS' with zero guarantee
// of real-time accuracy or upstream availability.
//
// This file is subject to the terms and conditions defined in the LICENSE
// file located in the root directory of this source code repository.
// Removal or modification of this legal notice constitutes copyright infringement.
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
}
