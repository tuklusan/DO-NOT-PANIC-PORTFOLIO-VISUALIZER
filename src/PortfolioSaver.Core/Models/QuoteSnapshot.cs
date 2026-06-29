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

public sealed class QuoteSnapshot
{
    public string Symbol { get; set; } = string.Empty;
    public decimal? Last { get; set; }
    public decimal? Change { get; set; }
    public decimal? ChangePercent { get; set; }
    public decimal? PreviousClose { get; set; }
    public string Currency { get; set; } = "USD";
    public MarketSession MarketSession { get; set; } = MarketSession.Unknown;
    public DateTimeOffset? ProviderTimestampUtc { get; set; }
    public DateTimeOffset FetchTimestampUtc { get; set; } = DateTimeOffset.UtcNow;
    public bool IsStale { get; set; }
}
