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
using PortfolioSaver.Config.ViewModels;
using PortfolioSaver.Core.Constants;
using PortfolioSaver.Core.Models;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class TickerGroupEditorViewModelTests
{
    [Fact]
    public void Constructor_TrimsTickerCountToMaxPerTape()
    {
        TickerGroup group = new()
        {
            Name = "Tape A",
            Tickers = Enumerable.Range(1, Defaults.MaxTickersPerTape + 5)
                .Select(index => new TickerItem { Symbol = $"SYM{index}", Enabled = true })
                .ToList()
        };

        TickerGroupEditorViewModel vm = new(group);

        Assert.Equal(Defaults.MaxTickersPerTape, vm.Tickers.Count);
    }

    [Fact]
    public void ToModel_EnforcesMaxTickersPerTape()
    {
        TickerGroupEditorViewModel vm = new();
        vm.Tickers.Clear();
        foreach (int index in Enumerable.Range(1, Defaults.MaxTickersPerTape + 3))
            vm.Tickers.Add(new TickerItemEditorViewModel(new TickerItem { Symbol = $"SYM{index}", Enabled = true }));

        TickerGroup model = vm.ToModel();

        Assert.Equal(Defaults.MaxTickersPerTape, model.Tickers.Count);
    }
}
