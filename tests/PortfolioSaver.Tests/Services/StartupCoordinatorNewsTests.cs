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
using System.Reflection;
using System.Linq;
using PortfolioSaver.Render.ViewModels;
using PortfolioSaver.Screensaver.Services;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class StartupCoordinatorNewsTests
{
    [Fact]
    public void BuildNews_PreservesOriginalHeadlineCountWithoutArtificialDuplication()
    {
        NewsFlasherViewModel news = InvokeBuildNews(["Headline A", "Headline B"]);

        Assert.Equal(2, news.Headlines.Count);
        Assert.All(news.Headlines, headline => Assert.False(string.IsNullOrWhiteSpace(headline.Text)));
        Assert.Equal("Headline A", news.Headlines[0].Text);
        Assert.Equal("Headline B", news.Headlines[1].Text);
    }

    [Fact]
    public void BuildNews_WhenInputEmpty_UsesWaitingMessage()
    {
        NewsFlasherViewModel news = InvokeBuildNews([]);

        Assert.True(news.Headlines.Count >= 1);
        Assert.Contains(news.Headlines, headline =>
            headline.Text.Contains("Waiting for summarized financial news", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildNews_OnlyIncludesClosingQuoteOncePerPlaybackSequence()
    {
        NewsFlasherViewModel news = InvokeBuildNews([
            "Macro headlines stay active across regions.",
            "[[CLOSING_QUOTE]] \"Nothing travels faster than the speed of light, with the possible exception of bad news, which obeys its own special laws.\""
        ]);

        Assert.Equal(2, news.Headlines.Count);
        Assert.Single(news.Headlines.Where(headline => headline.IsSupplemental));
        Assert.Contains(news.Headlines, headline => headline.Text.Contains("Nothing travels faster", StringComparison.Ordinal));
    }

    private static NewsFlasherViewModel InvokeBuildNews(IReadOnlyList<string> headlines)
    {
        MethodInfo? method = typeof(StartupCoordinator).GetMethod(
            "BuildNews",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        object? value = method!.Invoke(null, [headlines]);
        return Assert.IsType<NewsFlasherViewModel>(value);
    }
}
