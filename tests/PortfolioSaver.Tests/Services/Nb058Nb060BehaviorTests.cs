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
using System.Reflection;
using PortfolioSaver.Core.Constants;
using PortfolioSaver.Core.Enums;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Data.Services;
using PortfolioSaver.Render.Services;
using PortfolioSaver.Screensaver.Controls;
using PortfolioSaver.Screensaver.Services;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class Nb058Nb060BehaviorTests
{
    [Fact]
    public void BuildOrderedRuntimeSymbols_StagesMacrosThenWorldMarketsThenTapeSymbols()
    {
        StartupCoordinator coordinator = new();
        AppSettings settings = Defaults.CreateSettings();

        List<string> ordered = coordinator.BuildOrderedRuntimeSymbols(settings).ToList();

        IReadOnlyList<string> macros = StartupCoordinator.GetMacroIndicatorSymbols();
        IReadOnlyList<string> worldMarkets = FloatingClockBuilder.GetWorldIndexSymbols();
        IReadOnlyList<string> tapeSymbols = settings.Groups
            .Where(group => group.Enabled)
            .SelectMany(group => group.Tickers)
            .Where(ticker => ticker.Enabled && !string.IsNullOrWhiteSpace(ticker.Symbol))
            .Select(ticker => ticker.Symbol.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.Equal(macros, ordered.Take(macros.Count).ToList());

        int worldStartIndex = macros.Count;
        foreach (string worldSymbol in worldMarkets.Where(symbol => !macros.Contains(symbol, StringComparer.OrdinalIgnoreCase)))
        {
            Assert.Contains(worldSymbol, ordered.Skip(worldStartIndex).Take(worldMarkets.Count).ToList(), StringComparer.OrdinalIgnoreCase);
        }

        int firstTapeIndex = ordered.FindIndex(symbol => tapeSymbols.Contains(symbol, StringComparer.OrdinalIgnoreCase));
        Assert.True(firstTapeIndex >= macros.Count, "Tape symbols should not begin before the macro phase.");
        Assert.True(firstTapeIndex >= worldStartIndex, "Tape symbols should begin after the world-markets phase starts.");
    }

    [Fact]
    public void FormatPinnedStatusCountdown_ReturnsBlank_WhenCountdownUnavailable()
    {
        MethodInfo method = typeof(ScreensaverSceneControl).GetMethod(
            "FormatPinnedStatusCountdown",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not find FormatPinnedStatusCountdown.");

        string result = Assert.IsType<string>(method.Invoke(null, [MarketSession.Regular, new ExchangeCalendarStatus
        {
            HasCountdown = false,
            CountdownTo = ExchangeCountdownTarget.Unknown
        }]));

        Assert.Equal(string.Empty, result);
    }
}
