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
using System.Windows.Media;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Render.ViewModels;
using PortfolioSaver.Screensaver.Services;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class StartupCoordinatorGraphSelectionTests
{
    [Fact]
    public void SelectGraphTickerPairs_PrefersTopAbsoluteMovers_AndCapsAtSixteen()
    {
        StartupCoordinator coordinator = new();
        AppSettings settings = new()
        {
            EnableFloatingGraphs = true,
            Groups =
            [
                new TickerGroup
                {
                    Name = "Group A",
                    Enabled = true,
                    Tickers = Enumerable.Range(1, 10).Select(index => new TickerItem
                    {
                        Symbol = $"A{index:00}",
                        Enabled = true
                    }).ToList()
                },
                new TickerGroup
                {
                    Name = "Group B",
                    Enabled = true,
                    Tickers = Enumerable.Range(1, 10).Select(index => new TickerItem
                    {
                        Symbol = $"B{index:00}",
                        Enabled = true
                    }).ToList()
                }
            ]
        };

        Dictionary<string, QuoteSnapshot> quotes = new(StringComparer.OrdinalIgnoreCase);
        int score = 1;
        foreach (TickerItem ticker in settings.Groups.SelectMany(group => group.Tickers))
        {
            quotes[ticker.Symbol] = new QuoteSnapshot
            {
                Symbol = ticker.Symbol,
                Last = 100m + score,
                ChangePercent = score,
                FetchTimestampUtc = DateTimeOffset.UtcNow
            };
            score++;
        }

        coordinator.PrimeRuntimeQuotes(quotes);

        MethodInfo method = typeof(StartupCoordinator).GetMethod(
            "SelectGraphTickerPairs",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("SelectGraphTickerPairs not found.");

        object? raw = method.Invoke(coordinator, [settings]);
        IReadOnlyList<(TickerGroup Group, TickerItem Ticker)> selected =
            Assert.IsAssignableFrom<IReadOnlyList<(TickerGroup Group, TickerItem Ticker)>>(raw);

        Assert.Equal(16, selected.Count);

        HashSet<string> selectedSymbols = selected
            .Select(pair => pair.Ticker.Symbol)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.DoesNotContain("A01", selectedSymbols);
        Assert.DoesNotContain("A02", selectedSymbols);
        Assert.Contains("B10", selectedSymbols);
        Assert.Contains("B09", selectedSymbols);
        Assert.Equal(16, selectedSymbols.Count);
    }

    [Fact]
    public void TryCreateFallbackGraphSnapshot_UsesQuoteMemoryWhenHistoryIsUnavailable()
    {
        StartupCoordinator coordinator = new();
        coordinator.PrimeRuntimeQuotes(new Dictionary<string, QuoteSnapshot>(StringComparer.OrdinalIgnoreCase)
        {
            ["VOO"] = new QuoteSnapshot
            {
                Symbol = "VOO",
                Last = 512.34m,
                PreviousClose = 509.11m,
                FetchTimestampUtc = DateTimeOffset.UtcNow
            }
        });

        MethodInfo method = typeof(StartupCoordinator).GetMethod(
            "TryCreateFallbackGraphSnapshot",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("TryCreateFallbackGraphSnapshot not found.");

        object?[] args = ["VOO", 1, null];
        bool created = Assert.IsType<bool>(method.Invoke(coordinator, args));
        Assert.True(created);

        TickerHistorySnapshot snapshot = Assert.IsType<TickerHistorySnapshot>(args[2]);
        Assert.Equal("VOO", snapshot.Symbol);
        Assert.Equal(1, snapshot.LookbackDays);
        Assert.Equal(2, snapshot.Points.Count);
        Assert.Equal(509.11m, snapshot.Points[0].Close);
        Assert.Equal(512.34m, snapshot.Points[1].Close);
    }

    [Fact]
    public void BuildGraph_ReusesCachedGraphWhenHistorySnapshotIsUnchanged()
    {
        StartupCoordinator coordinator = new();
        MethodInfo method = GetBuildGraphMethod();
        AppSettings settings = new() { EnableBouncingGraphCards = true };
        TickerHistorySnapshot snapshot = CreateHistorySnapshot("VOO", 100m, 101m);

        FloatingGraphViewModel first = Assert.IsType<FloatingGraphViewModel>(method.Invoke(coordinator, ["CORE", snapshot, settings]));
        PointCollection originalGreenPoints = first.GreenPoints;
        FloatingGraphViewModel second = Assert.IsType<FloatingGraphViewModel>(method.Invoke(coordinator, ["CORE", CreateHistorySnapshot("VOO", 100m, 101m), settings]));

        Assert.Same(first, second);
        Assert.Same(originalGreenPoints, second.GreenPoints);
        Assert.True(second.BounceWithinViewport);
    }

    [Fact]
    public void BuildGraph_RebuildsCachedGraphWhenHistorySnapshotChanges()
    {
        StartupCoordinator coordinator = new();
        MethodInfo method = GetBuildGraphMethod();
        AppSettings settings = new() { EnableBouncingGraphCards = true };

        FloatingGraphViewModel first = Assert.IsType<FloatingGraphViewModel>(method.Invoke(coordinator, ["CORE", CreateHistorySnapshot("VOO", 100m, 101m), settings]));
        FloatingGraphViewModel second = Assert.IsType<FloatingGraphViewModel>(method.Invoke(coordinator, ["CORE", CreateHistorySnapshot("VOO", 100m, 102m), settings]));

        Assert.NotSame(first, second);
        Assert.NotSame(first.GreenPoints, second.GreenPoints);
    }

    [Fact]
    public void BuildGraph_RebuildsCachedGraphWhenFetchTimestampChanges()
    {
        StartupCoordinator coordinator = new();
        MethodInfo method = GetBuildGraphMethod();
        AppSettings settings = new();
        TickerHistorySnapshot firstSnapshot = CreateHistorySnapshot("VOO", 100m, 101m);
        TickerHistorySnapshot secondSnapshot = CreateHistorySnapshot("VOO", 100m, 101m);
        secondSnapshot.FetchTimestampUtc = firstSnapshot.FetchTimestampUtc.AddMinutes(1);

        FloatingGraphViewModel first = Assert.IsType<FloatingGraphViewModel>(method.Invoke(coordinator, ["CORE", firstSnapshot, settings]));
        FloatingGraphViewModel second = Assert.IsType<FloatingGraphViewModel>(method.Invoke(coordinator, ["CORE", secondSnapshot, settings]));

        Assert.NotSame(first, second);
    }

    [Fact]
    public void BuildGraph_RebuildsCachedGraphWhenBounceSettingChanges()
    {
        StartupCoordinator coordinator = new();
        MethodInfo method = GetBuildGraphMethod();
        AppSettings firstSettings = new() { EnableBouncingGraphCards = true };
        AppSettings secondSettings = new() { EnableBouncingGraphCards = false };

        FloatingGraphViewModel first = Assert.IsType<FloatingGraphViewModel>(method.Invoke(coordinator, ["CORE", CreateHistorySnapshot("VOO", 100m, 101m), firstSettings]));
        FloatingGraphViewModel second = Assert.IsType<FloatingGraphViewModel>(method.Invoke(coordinator, ["CORE", CreateHistorySnapshot("VOO", 100m, 101m), secondSettings]));

        Assert.NotSame(first, second);
        Assert.True(first.BounceWithinViewport);
        Assert.False(second.BounceWithinViewport);
    }

    [Fact]
    public void BuildGraph_CacheKeyDoesNotCollideWhenNamesContainSeparators()
    {
        StartupCoordinator coordinator = new();
        MethodInfo method = GetBuildGraphMethod();
        AppSettings settings = new();

        FloatingGraphViewModel first = Assert.IsType<FloatingGraphViewModel>(method.Invoke(coordinator, ["CORE|VOO", CreateHistorySnapshot("ALT", 100m, 101m), settings]));
        FloatingGraphViewModel second = Assert.IsType<FloatingGraphViewModel>(method.Invoke(coordinator, ["CORE", CreateHistorySnapshot("VOO|ALT", 100m, 101m), settings]));

        Assert.NotSame(first, second);
    }

    [Fact]
    public void BuildGraph_CacheKeyTreatsSymbolCaseInsensitively()
    {
        StartupCoordinator coordinator = new();
        MethodInfo method = GetBuildGraphMethod();
        AppSettings settings = new();

        FloatingGraphViewModel first = Assert.IsType<FloatingGraphViewModel>(method.Invoke(coordinator, ["CORE", CreateHistorySnapshot("VOO", 100m, 101m), settings]));
        FloatingGraphViewModel second = Assert.IsType<FloatingGraphViewModel>(method.Invoke(coordinator, ["CORE", CreateHistorySnapshot("voo", 100m, 101m), settings]));

        Assert.Same(first, second);
    }

    [Fact]
    public void BuildGraph_CacheEvictsLeastRecentlyUsedGraph()
    {
        StartupCoordinator coordinator = new();
        MethodInfo method = GetBuildGraphMethod();
        AppSettings settings = new();
        FloatingGraphViewModel first = Assert.IsType<FloatingGraphViewModel>(method.Invoke(coordinator, ["CORE", CreateHistorySnapshot("S00", 100m, 101m), settings]));

        for (int index = 1; index <= 64; index++)
        {
            _ = method.Invoke(coordinator, ["CORE", CreateHistorySnapshot($"S{index:00}", 100m, 101m), settings]);
        }

        Assert.Equal(64, GetGraphBuildCacheCount(coordinator));
        FloatingGraphViewModel rebuiltFirst = Assert.IsType<FloatingGraphViewModel>(method.Invoke(coordinator, ["CORE", CreateHistorySnapshot("S00", 100m, 101m), settings]));

        Assert.NotSame(first, rebuiltFirst);
        Assert.Equal(64, GetGraphBuildCacheCount(coordinator));
    }

    private static MethodInfo GetBuildGraphMethod()
        => typeof(StartupCoordinator).GetMethod("BuildGraph", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("BuildGraph not found.");

    private static int GetGraphBuildCacheCount(StartupCoordinator coordinator)
    {
        FieldInfo field = typeof(StartupCoordinator).GetField("_graphBuildCache", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(nameof(StartupCoordinator), "_graphBuildCache");
        if (field.GetValue(coordinator) is not System.Collections.ICollection cache)
            throw new InvalidOperationException("Graph build cache was not available.");

        return cache.Count;
    }

    private static TickerHistorySnapshot CreateHistorySnapshot(string symbol, params decimal[] closes)
    {
        DateTimeOffset start = new(2000, 1, 1, 12, 0, 0, TimeSpan.Zero);
        return new TickerHistorySnapshot
        {
            Symbol = symbol,
            FetchTimestampUtc = start,
            LookbackDays = 1,
            Points = closes.Select((close, index) => new HistoricalPricePoint
            {
                TimestampUtc = start.AddMinutes(index),
                Close = close
            }).ToList()
        };
    }
}
