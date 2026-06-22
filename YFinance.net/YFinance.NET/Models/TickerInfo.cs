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
        RegularMarketPrice.HasValue && RegularMarketPreviousClose is not null && RegularMarketPreviousClose.Value != 0m
            ? ((RegularMarketPrice.Value - RegularMarketPreviousClose.Value) / RegularMarketPreviousClose.Value) * 100m
            : RegularMarketChangePercent;
}
