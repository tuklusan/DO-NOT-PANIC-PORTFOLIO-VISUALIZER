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
using PortfolioSaver.Core.Models;
using PortfolioSaver.Core.Constants;
using PortfolioSaver.Screensaver.Services;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class QuoteRefreshPolicyTests
{
    [Fact]
    public void GetRefreshPollingInterval_RemainsOneSecondUiDispatchCadence()
    {
        AppSettings settings = new()
        {
            RefreshSecondsPortfolio = 900,
            RefreshSecondsOffHours = 1800
        };

        TimeSpan interval = QuoteRefreshPolicy.GetRefreshPollingInterval(settings, new DateTimeOffset(2026, 6, 5, 14, 0, 0, TimeSpan.Zero));

        Assert.Equal(TimeSpan.FromSeconds(1), interval);
    }

    [Fact]
    public void GetEffectiveRefreshWindow_RemainsOneSecondUiDispatchCadence()
    {
        AppSettings settings = new()
        {
            RefreshSecondsPortfolio = 900,
            RefreshSecondsOffHours = 1800
        };

        TimeSpan window = QuoteRefreshPolicy.GetEffectiveRefreshWindow(settings, new DateTimeOffset(2026, 6, 5, 14, 0, 0, TimeSpan.Zero));

        Assert.Equal(TimeSpan.FromSeconds(1), window);
    }

    [Fact]
    public void GetConfiguredRefreshWindow_UsesPortfolioAndOffHoursSettingsForFreshnessPolicy()
    {
        AppSettings settings = new()
        {
            RefreshSecondsPortfolio = 600,
            RefreshSecondsOffHours = 1800
        };

        TimeSpan openWindow = QuoteRefreshPolicy.GetConfiguredRefreshWindow(settings, new DateTimeOffset(2026, 6, 5, 14, 0, 0, TimeSpan.Zero));
        TimeSpan closedWindow = QuoteRefreshPolicy.GetConfiguredRefreshWindow(settings, new DateTimeOffset(2026, 6, 6, 14, 0, 0, TimeSpan.Zero));

        Assert.Equal(TimeSpan.FromSeconds(600), openWindow);
        Assert.Equal(TimeSpan.FromSeconds(1800), closedWindow);
    }

    [Fact]
    public void GetConfiguredRefreshWindow_ClampsLegacyRefreshSettings()
    {
        AppSettings settings = new()
        {
            RefreshSecondsPortfolio = 0,
            RefreshSecondsOffHours = 999999
        };

        TimeSpan openWindow = QuoteRefreshPolicy.GetConfiguredRefreshWindow(settings, new DateTimeOffset(2026, 6, 5, 14, 0, 0, TimeSpan.Zero));
        TimeSpan closedWindow = QuoteRefreshPolicy.GetConfiguredRefreshWindow(settings, new DateTimeOffset(2026, 6, 6, 14, 0, 0, TimeSpan.Zero));

        Assert.Equal(TimeSpan.FromSeconds(Defaults.MinRefreshSeconds), openWindow);
        Assert.Equal(TimeSpan.FromSeconds(Defaults.MaxRefreshSeconds), closedWindow);
    }

    [Fact]
    public void GetHardStaleThreshold_RemainsFixedFifteenMinuteCompatibilityThreshold()
    {
        AppSettings settings = new()
        {
            RefreshSecondsPortfolio = Defaults.MaxRefreshSeconds,
            RefreshSecondsOffHours = Defaults.MaxRefreshSeconds
        };

        TimeSpan threshold = QuoteRefreshPolicy.GetHardStaleThreshold(settings, new DateTimeOffset(2026, 6, 5, 14, 0, 0, TimeSpan.Zero));

        Assert.Equal(TimeSpan.FromMinutes(15), threshold);
    }

    [Theory]
    [InlineData(2026, 6, 5, 13, 29, 59, 1800)]
    [InlineData(2026, 6, 5, 13, 30, 0, 600)]
    [InlineData(2026, 6, 5, 19, 59, 59, 600)]
    [InlineData(2026, 6, 5, 20, 0, 0, 1800)]
    [InlineData(2026, 6, 5, 20, 0, 1, 1800)]
    public void GetConfiguredRefreshWindow_UsesNewYorkMarketWindowWithExclusiveClose(
        int year,
        int month,
        int day,
        int hour,
        int minute,
        int second,
        int expectedSeconds)
    {
        AppSettings settings = new()
        {
            RefreshSecondsPortfolio = 600,
            RefreshSecondsOffHours = 1800
        };

        TimeSpan window = QuoteRefreshPolicy.GetConfiguredRefreshWindow(settings, new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.Zero));

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), window);
    }
}
