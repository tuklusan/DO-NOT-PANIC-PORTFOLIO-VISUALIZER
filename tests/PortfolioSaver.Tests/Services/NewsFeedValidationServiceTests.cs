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
using PortfolioSaver.Config.Services;
using PortfolioSaver.Core.Constants;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class NewsFeedValidationServiceTests
{
    private readonly NewsFeedValidationService _service = new();

    [Fact]
    public async Task ValidateAsync_InvalidUrl_ResetsToDefault()
    {
        NewsFeedValidationResult result = await _service.ValidateAsync(
            "not a url",
            timeoutSeconds: 3,
            networkAvailable: true);

        Assert.False(result.IsValid);
        Assert.True(result.WasResetToDefault);
        Assert.Equal(Defaults.DefaultNewsFeedUrl, result.ResolvedFeedUrl);
    }

    [Fact]
    public async Task ValidateAsync_NonHttpUrl_ResetsToDefault()
    {
        NewsFeedValidationResult result = await _service.ValidateAsync(
            "file:///C:/temp/rss.xml",
            timeoutSeconds: 3,
            networkAvailable: true);

        Assert.False(result.IsValid);
        Assert.True(result.WasResetToDefault);
        Assert.Equal(Defaults.DefaultNewsFeedUrl, result.ResolvedFeedUrl);
    }

    [Fact]
    public async Task ValidateAsync_UnreachableHttpUrl_ResetsToDefault()
    {
        NewsFeedValidationResult result = await _service.ValidateAsync(
            "http://127.0.0.1:1/rss",
            timeoutSeconds: 3,
            networkAvailable: true);

        Assert.False(result.IsValid);
        Assert.True(result.WasResetToDefault);
        Assert.Equal(Defaults.DefaultNewsFeedUrl, result.ResolvedFeedUrl);
    }

    [Fact]
    public async Task ValidateAsync_NoNetwork_SkipsValidationWithoutReset()
    {
        const string feed = "https://example.com/feed.xml";
        NewsFeedValidationResult result = await _service.ValidateAsync(
            feed,
            timeoutSeconds: 3,
            networkAvailable: false);

        Assert.True(result.IsValid);
        Assert.True(result.ValidationSkipped);
        Assert.False(result.WasResetToDefault);
        Assert.Equal(feed, result.ResolvedFeedUrl);
    }
}
