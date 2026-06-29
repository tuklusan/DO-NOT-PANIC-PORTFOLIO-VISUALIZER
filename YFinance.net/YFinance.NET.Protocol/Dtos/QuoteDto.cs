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
namespace YFinance.NET.Protocol.Dtos;

public sealed record QuoteDto(
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
    long? MarketCap,
    long? RegularMarketVolume,
    DateTimeOffset FetchTimestampUtc,
    CacheMetadataDto Cache);
