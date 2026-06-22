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
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class Nb051BehaviorTests
{
    [Fact]
    public void ScreensaverSceneControl_UsesIndependentWorldMarketsLane()
    {
        string source = File.ReadAllText(Path.Combine(GetRepoRoot(), "src", "PortfolioSaver.Presentation", "Controls", "ScreensaverSceneControl.xaml.cs"));

        Assert.Contains("StartWorldMarketsLane();", source, StringComparison.Ordinal);
        Assert.Contains("RunWorldMarketsLaneAsync", source, StringComparison.Ordinal);
        Assert.Contains("BuildWorldMarketsLaneSnapshotAsync", source, StringComparison.Ordinal);
        Assert.Contains("ApplyWorldMarketsLaneSnapshot", source, StringComparison.Ordinal);
        Assert.Contains("WorldMarketsLaneMinimumRefreshInterval", source, StringComparison.Ordinal);
        Assert.Contains("TimeSpan remaining = WorldMarketsLaneMinimumRefreshInterval", source, StringComparison.Ordinal);
        Assert.Contains("\"WorldMarketsRefreshStart\"", source, StringComparison.Ordinal);
        Assert.Contains("\"WorldMarketsFetchComplete\"", source, StringComparison.Ordinal);
        Assert.Contains("\"WorldMarketsMergeComplete\"", source, StringComparison.Ordinal);
        Assert.Contains("\"WorldMarketsUiPatchComplete\"", source, StringComparison.Ordinal);
        Assert.Contains("QueueWorldMarketsRefresh(refreshAncillary: false, reason: \"quote-delta\");", source, StringComparison.Ordinal);
        Assert.Contains("HasMeaningfulWorldMarketDelta(previousQuotes, deltaQuotes)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyClockMarketData(force: false)", source, StringComparison.Ordinal);
    }

    private static string GetRepoRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "PortfolioScreensaver.sln")))
                return current.FullName;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("PortfolioScreensaver.sln not found from test base directory.");
    }
}
