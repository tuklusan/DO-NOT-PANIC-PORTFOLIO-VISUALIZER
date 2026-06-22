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
