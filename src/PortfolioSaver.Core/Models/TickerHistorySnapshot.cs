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
using PortfolioSaver.Core.Enums;

namespace PortfolioSaver.Core.Models;

public sealed class TickerHistorySnapshot
{
    public string Symbol { get; set; } = string.Empty;
    public DateTimeOffset FetchTimestampUtc { get; set; } = DateTimeOffset.UtcNow;
    public int LookbackDays { get; set; } = 14;
    public GraphSeriesKind SeriesKind { get; set; } = GraphSeriesKind.Intraday;
    public string ExchangeTimeZoneId { get; set; } = "UTC";
    public List<HistoricalPricePoint> Points { get; set; } = [];

    public bool IsFresh(TimeSpan maxAge) => DateTimeOffset.UtcNow - FetchTimestampUtc <= maxAge;
}
