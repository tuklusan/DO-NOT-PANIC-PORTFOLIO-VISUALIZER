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
using PortfolioSaver.Core.Constants;
using PortfolioSaver.Core.Enums;

namespace PortfolioSaver.Core.Models;

public sealed class AppSettings
{
    public int RefreshSecondsPortfolio { get; set; } = 300;
    public int RefreshSecondsOffHours { get; set; } = 300;
    public int HttpTimeoutSeconds { get; set; } = 10;
    public NewsScrollerMode NewsScrollerMode { get; set; } = NewsScrollerMode.RssFeed;
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

    // Summarized news uses an OpenAI-compatible AI endpoint. The property names remain
    // DeepSeek-prefixed for config compatibility, but defaults now target OpenRouter.
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

    /// <summary>
    /// Creates a detached copy used by config change detection.
    /// </summary>
    /// <remarks>
    /// Keep this explicit clone in sync with AppSettings, TickerGroup, and TickerItem properties.
    /// AppSettingsNormalizerTests.Clone_CopiesAllWritableSettingsGraphPropertiesAndDeepCopiesLists
    /// guards against silent omissions when these models grow.
    /// </remarks>
    public AppSettings Clone() => new()
    {
        RefreshSecondsPortfolio = RefreshSecondsPortfolio,
        RefreshSecondsOffHours = RefreshSecondsOffHours,
        HttpTimeoutSeconds = HttpTimeoutSeconds,
        NewsScrollerMode = NewsScrollerMode,
        DeepSeekWritingStyle = DeepSeekWritingStyle,
        NewsFeedUrl = NewsFeedUrl,
        NewsRefreshMinutes = NewsRefreshMinutes,
        BackgroundImageFolder = BackgroundImageFolder,
        UseCustomBackgroundImageFolder = UseCustomBackgroundImageFolder,
        CustomBackgroundImageFolder = CustomBackgroundImageFolder,
        BackgroundChangeSeconds = BackgroundChangeSeconds,
        ShuffleBackgrounds = ShuffleBackgrounds,
        DimOpacity = DimOpacity,
        LayoutPreset = LayoutPreset,
        DeepSeekApiKey = DeepSeekApiKey,
        DeepSeekEndpointUrl = DeepSeekEndpointUrl,
        DeepSeekModelId = DeepSeekModelId,
        MarketCalendarRefreshHours = MarketCalendarRefreshHours,
        EnableFloatingGraphs = EnableFloatingGraphs,
        HistoricalLookbackDays = HistoricalLookbackDays,
        HistoricalRefreshHours = HistoricalRefreshHours,
        MaxFloatingGraphsPerTape = MaxFloatingGraphsPerTape,
        HistoricalCacheRootFolder = HistoricalCacheRootFolder,
        EnableBouncingGraphCards = EnableBouncingGraphCards,
        FloatingGraphVelocityMin = FloatingGraphVelocityMin,
        FloatingGraphVelocityMax = FloatingGraphVelocityMax,
        EnableFloatingClock = EnableFloatingClock,
        ClockRefreshSeconds = ClockRefreshSeconds,
        BackgroundIncludeSubfolders = BackgroundIncludeSubfolders,
        Groups = (Groups ?? []).Select(CloneGroup).ToList()
    };

    private static TickerGroup CloneGroup(TickerGroup source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        Speed = source.Speed,
        Direction = source.Direction,
        RenderMode = source.RenderMode,
        RowHeight = source.RowHeight,
        Enabled = source.Enabled,
        Tickers = (source.Tickers ?? []).Select(CloneTicker).ToList()
    };

    private static TickerItem CloneTicker(TickerItem source) => new()
    {
        Symbol = source.Symbol,
        DisplayName = source.DisplayName,
        Quantity = source.Quantity,
        CostBasis = source.CostBasis,
        Currency = source.Currency,
        Enabled = source.Enabled
    };
}
