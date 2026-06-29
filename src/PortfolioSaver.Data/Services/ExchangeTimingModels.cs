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
using YFinance.NET.Models;

namespace PortfolioSaver.Data.Services;

public sealed class ExchangeCalendarRequest
{
    public string CityKey { get; set; } = string.Empty;
    public string ExchangeCode { get; set; } = string.Empty;
    public string ExchangeName { get; set; } = string.Empty;
    public string ExchangeSymbol { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = string.Empty;
    public string AlternateTimeZoneId { get; set; } = string.Empty;
}

public sealed class ExchangeCalendarSet
{
    public DateTimeOffset GeneratedUtc { get; set; } = DateTimeOffset.MinValue;
    public string Source { get; set; } = "YFinance";
    public Dictionary<string, ExchangeTradingCalendar> CalendarsByCityKey { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void Overlay(ExchangeCalendarSet? overlay)
    {
        if (overlay is null)
            return;

        foreach ((string cityKey, ExchangeTradingCalendar incoming) in overlay.CalendarsByCityKey)
            CalendarsByCityKey[cityKey] = incoming.Clone();

        if (overlay.GeneratedUtc > GeneratedUtc)
            GeneratedUtc = overlay.GeneratedUtc;
        if (!string.IsNullOrWhiteSpace(overlay.Source))
            Source = overlay.Source;
    }

    public ExchangeTradingCalendar? TryGetByCityKey(string cityKey)
        => CalendarsByCityKey.TryGetValue(cityKey, out ExchangeTradingCalendar? calendar) ? calendar : null;
}

public sealed class ExchangeTradingCalendar
{
    public string CityKey { get; set; } = string.Empty;
    public string ExchangeCode { get; set; } = string.Empty;
    public string ExchangeName { get; set; } = string.Empty;
    public string ExchangeSymbol { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = string.Empty;
    public string AlternateTimeZoneId { get; set; } = string.Empty;
    public string Source { get; set; } = "YFinance";
    public DateTimeOffset? RegularMarketTimeUtc { get; set; }
    public CurrentTradingPeriods? CurrentTradingPeriod { get; set; }

    public ExchangeTradingCalendar Clone()
        => new()
        {
            CityKey = CityKey,
            ExchangeCode = ExchangeCode,
            ExchangeName = ExchangeName,
            ExchangeSymbol = ExchangeSymbol,
            TimeZoneId = TimeZoneId,
            AlternateTimeZoneId = AlternateTimeZoneId,
            Source = Source,
            RegularMarketTimeUtc = RegularMarketTimeUtc,
            CurrentTradingPeriod = CurrentTradingPeriod is null
                ? null
                : new CurrentTradingPeriods(
                    CurrentTradingPeriod.Pre,
                    CurrentTradingPeriod.Regular,
                    CurrentTradingPeriod.Post)
        };
}

public sealed class ExchangeCalendarStatus
{
    public MarketSession Session { get; set; } = MarketSession.Unknown;
    public bool IsOpen { get; set; }
    public TimeSpan Countdown { get; set; }
    public ExchangeCountdownTarget CountdownTo { get; set; } = ExchangeCountdownTarget.Unknown;
    public bool HasCountdown { get; set; }
}

public enum ExchangeCountdownTarget
{
    Unknown,
    Open,
    Close,
    SessionEnd
}
