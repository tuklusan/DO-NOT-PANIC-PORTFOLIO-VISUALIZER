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
namespace YFinance.NET.Models;

public sealed record TickerInfo(
    string Symbol,
    string? ShortName,
    string? LongName,
    string? DisplayName,
    string? Currency,
    string? Exchange,
    string? ExchangeTimezoneName,
    string? ExchangeTimezoneShortName,
    string? QuoteType,
    string? MarketState,
    decimal? RegularMarketPrice,
    decimal? RegularMarketPreviousClose,
    decimal? RegularMarketOpen,
    decimal? RegularMarketDayHigh,
    decimal? RegularMarketDayLow,
    decimal? RegularMarketChange,
    decimal? RegularMarketChangePercent,
    decimal? FiftyTwoWeekLow,
    decimal? FiftyTwoWeekHigh,
    decimal? FiftyDayAverage,
    decimal? TwoHundredDayAverage,
    long? RegularMarketVolume,
    long? AverageVolume,
    long? AverageVolume10Day,
    long? SharesOutstanding,
    long? MarketCap,
    decimal? TrailingPe,
    decimal? ForwardPe,
    decimal? DividendYield,
    string? Sector,
    string? Industry,
    string? LongBusinessSummary,
    string? Website,
    IReadOnlyDictionary<string, object?> FlatFields)
{
    public decimal? ComputedChangePercent =>
        QuoteMath.ComputeChangePercent(RegularMarketPrice, RegularMarketPreviousClose, RegularMarketChange, RegularMarketChangePercent);
}
