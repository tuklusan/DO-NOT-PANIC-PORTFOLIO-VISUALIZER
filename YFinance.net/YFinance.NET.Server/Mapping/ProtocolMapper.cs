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
using YFinance.NET.Models;
using YFinance.NET.Protocol.Dtos;

namespace YFinance.NET.Server.Mapping;

internal static class ProtocolMapper
{
    public static QuoteDto MapQuote(QuoteSnapshot quote)
        => new(
            quote.Symbol,
            quote.ShortName,
            quote.LongName,
            quote.DisplayName,
            quote.Currency,
            quote.Exchange,
            quote.ExchangeTimezoneName,
            quote.ExchangeTimezoneShortName,
            quote.QuoteType,
            quote.MarketState,
            quote.RegularMarketPrice,
            quote.RegularMarketPreviousClose,
            quote.RegularMarketOpen,
            quote.RegularMarketDayHigh,
            quote.RegularMarketDayLow,
            quote.RegularMarketChange,
            quote.ComputedChangePercent,
            quote.MarketCap,
            quote.RegularMarketVolume,
            DateTimeOffset.UtcNow,
            new CacheMetadataDto("server", 0, false));

    public static HistoryResponseDto MapHistory(HistoryResponse response)
        => new(
            response.Symbol,
            response.Bars.Select(bar => new HistoryBarDto(bar.Timestamp, bar.Open, bar.High, bar.Low, bar.Close, bar.Volume)).ToList(),
            MapHistoryMetadata(response.Metadata),
            new CacheMetadataDto("server", 0, false));

    public static MarketTimingDto MapMarketTiming(MarketTimingSnapshot timing)
        => new(
            timing.Symbol,
            timing.ExchangeName,
            timing.ExchangeTimezoneName,
            timing.InstrumentType,
            timing.RegularMarketTimeUtc,
            timing.GmtOffsetSeconds,
            MapCurrentTradingPeriods(timing.CurrentTradingPeriod),
            timing.ExchangeLocalDate,
            timing.FetchedUtc,
            new CacheMetadataDto("server", 0, false));

    public static TickerInfoDto MapTickerInfo(TickerInfo info)
        => new(
            info.Symbol,
            info.ShortName,
            info.LongName,
            info.DisplayName,
            info.Currency,
            info.Exchange,
            info.ExchangeTimezoneName,
            info.ExchangeTimezoneShortName,
            info.QuoteType,
            info.MarketState,
            info.RegularMarketPrice,
            info.RegularMarketPreviousClose,
            info.RegularMarketChange,
            info.ComputedChangePercent,
            info.MarketCap,
            info.Sector,
            info.Industry,
            info.Website,
            new CacheMetadataDto("server", 0, false));

    private static HistoryMetadataDto? MapHistoryMetadata(HistoryMetadata? metadata)
        => metadata is null
            ? null
            : new HistoryMetadataDto(
                metadata.ExchangeName,
                metadata.InstrumentType,
                metadata.Currency,
                metadata.ExchangeTimezoneName,
                null,
                metadata.GmtOffsetSeconds,
                metadata.RegularMarketTimeUtc,
                MapCurrentTradingPeriods(metadata.CurrentTradingPeriod));

    private static CurrentTradingPeriodsDto? MapCurrentTradingPeriods(CurrentTradingPeriods? periods)
        => periods is null
            ? null
            : new CurrentTradingPeriodsDto(MapTradingPeriod(periods.Pre), MapTradingPeriod(periods.Regular), MapTradingPeriod(periods.Post));

    private static TradingPeriodWindowDto? MapTradingPeriod(TradingPeriodWindow? period)
        => period is null ? null : new TradingPeriodWindowDto(period.StartUtc, period.EndUtc, period.GmtOffsetSeconds);
}
