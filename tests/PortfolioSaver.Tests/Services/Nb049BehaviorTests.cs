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

public sealed class Nb049BehaviorTests
{
    [Fact]
    public void ScreensaverSceneControl_UsesIndependentMacroRefreshLane()
    {
        string source = File.ReadAllText(Path.Combine(GetRepoRoot(), "src", "PortfolioSaver.Presentation", "Controls", "ScreensaverSceneControl.xaml.cs"));

        Assert.Contains("StartMacroLane();", source, StringComparison.Ordinal);
        Assert.Contains("RunMacroLaneAsync", source, StringComparison.Ordinal);
        Assert.Contains("BuildMacroLaneSnapshotAsync", source, StringComparison.Ordinal);
        Assert.Contains("ApplyMacroLaneSnapshot", source, StringComparison.Ordinal);
        Assert.Contains("MacroLaneMinimumRefreshInterval", source, StringComparison.Ordinal);
        Assert.Contains("TimeSpan remaining = MacroLaneMinimumRefreshInterval", source, StringComparison.Ordinal);
        Assert.Contains("\"MacroRefreshStart\"", source, StringComparison.Ordinal);
        Assert.Contains("\"MacroUiPatchComplete\"", source, StringComparison.Ordinal);
        Assert.Contains("QueueMacroRefresh(\"quote-delta\");", source, StringComparison.Ordinal);
        Assert.Contains("IReadOnlyDictionary<string, QuoteSnapshot> previousQuotes = _latestQuotes;", source, StringComparison.Ordinal);
        Assert.Contains("HasMeaningfulMacroDelta(previousQuotes, deltaQuotes)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateStatusMacroMeters(force: true);", source, StringComparison.Ordinal);
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
