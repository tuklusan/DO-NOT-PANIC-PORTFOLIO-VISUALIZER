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
