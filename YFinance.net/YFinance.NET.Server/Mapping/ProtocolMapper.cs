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
            quote.ComputedChange,
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
            info.ComputedChange,
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
