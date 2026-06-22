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

namespace PortfolioSaver.Core.Models;

public sealed class AppSettings
{
    public int RefreshSecondsPortfolio { get; set; } = 300;
    public int RefreshSecondsOffHours { get; set; } = 300;
    public int HttpTimeoutSeconds { get; set; } = 10;
    public NewsScrollerMode NewsScrollerMode { get; set; } = NewsScrollerMode.SummarizedFinancialNews;
    public DeepSeekWritingStyle DeepSeekWritingStyle { get; set; } = DeepSeekWritingStyle.DouglasAdams;
    public string NewsFeedUrl { get; set; } = "https://finance.yahoo.com/news/rss";
    public int NewsRefreshMinutes { get; set; } = 15;

    public string BackgroundImageFolder { get; set; } = string.Empty;
    public bool UseCustomBackgroundImageFolder { get; set; }
    public string CustomBackgroundImageFolder { get; set; } = string.Empty;
    public int BackgroundChangeSeconds { get; set; } = 300;
    public bool ShuffleBackgrounds { get; set; } = true;
    public double DimOpacity { get; set; } = 0.55;
    public LayoutPreset LayoutPreset { get; set; } = LayoutPreset.UltrawideDefault;

    // DeepSeek remains user-configurable for summarized news and can still be overlaid from
    // protected local storage or environment variables.
    public string DeepSeekApiKey { get; set; } = string.Empty;
    public string DeepSeekEndpointUrl { get; set; } = Defaults.DefaultDeepSeekEndpointUrl;
    public string DeepSeekModelId { get; set; } = Defaults.DefaultDeepSeekModelId;
    public int MarketCalendarRefreshHours { get; set; } = 12;

    public bool EnableFloatingGraphs { get; set; } = true;
    public int HistoricalLookbackDays { get; set; } = 14;
    public int HistoricalRefreshHours { get; set; } = 12;
    public int MaxFloatingGraphsPerTape { get; set; } = 4;
    public string HistoricalCacheRootFolder { get; set; } = string.Empty;

    public bool EnableBouncingGraphCards { get; set; } = true;
    public double FloatingGraphVelocityMin { get; set; } = 22;
    public double FloatingGraphVelocityMax { get; set; } = 48;
    public bool EnableFloatingClock { get; set; } = true;
    public int ClockRefreshSeconds { get; set; } = 1;

    public bool BackgroundIncludeSubfolders { get; set; } = true;
    public List<TickerGroup> Groups { get; set; } = [];
}
