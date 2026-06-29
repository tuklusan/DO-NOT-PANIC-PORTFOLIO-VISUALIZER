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
using PortfolioSaver.Data.Services;
using Xunit;
using YFinance.NET.Models;

namespace PortfolioSaver.Tests.Services;

public sealed class YFinanceExchangeTimingServiceTests
{
    private readonly YFinanceExchangeTimingService _service = new();

    [Fact]
    public void ResolveStatus_DuringRegularSession_UsesYahooRegularWindow()
    {
        ExchangeTradingCalendar calendar = CreateCalendar(
            regular: Window("2026-05-20T13:30:00Z", "2026-05-20T20:00:00Z"),
            pre: Window("2026-05-20T09:00:00Z", "2026-05-20T13:30:00Z"),
            post: Window("2026-05-20T20:00:00Z", "2026-05-21T00:00:00Z"));

        ExchangeCalendarStatus status = _service.ResolveStatus(calendar, DateTimeOffset.Parse("2026-05-20T15:00:00Z"));

        Assert.Equal(MarketSession.Regular, status.Session);
        Assert.True(status.IsOpen);
        Assert.Equal(ExchangeCountdownTarget.Close, status.CountdownTo);
        Assert.True(status.HasCountdown);
        Assert.Equal(TimeSpan.FromHours(5), status.Countdown);
    }

    [Fact]
    public void ResolveStatus_DuringPreMarket_CountsDownToRegularOpen()
    {
        ExchangeTradingCalendar calendar = CreateCalendar(
            regular: Window("2026-05-20T13:30:00Z", "2026-05-20T20:00:00Z"),
            pre: Window("2026-05-20T09:00:00Z", "2026-05-20T13:30:00Z"),
            post: Window("2026-05-20T20:00:00Z", "2026-05-21T00:00:00Z"));

        ExchangeCalendarStatus status = _service.ResolveStatus(calendar, DateTimeOffset.Parse("2026-05-20T13:00:00Z"));

        Assert.Equal(MarketSession.PreMarket, status.Session);
        Assert.False(status.IsOpen);
        Assert.Equal(ExchangeCountdownTarget.Open, status.CountdownTo);
        Assert.True(status.HasCountdown);
        Assert.Equal(TimeSpan.FromMinutes(30), status.Countdown);
    }

    [Fact]
    public void ResolveStatus_DuringAfterHours_CountsDownToYahooPostEnd()
    {
        ExchangeTradingCalendar calendar = CreateCalendar(
            regular: Window("2026-05-20T13:30:00Z", "2026-05-20T20:00:00Z"),
            pre: Window("2026-05-20T09:00:00Z", "2026-05-20T13:30:00Z"),
            post: Window("2026-05-20T20:00:00Z", "2026-05-21T00:00:00Z"));

        ExchangeCalendarStatus status = _service.ResolveStatus(calendar, DateTimeOffset.Parse("2026-05-20T21:00:00Z"));

        Assert.Equal(MarketSession.AfterHours, status.Session);
        Assert.False(status.IsOpen);
        Assert.Equal(ExchangeCountdownTarget.SessionEnd, status.CountdownTo);
        Assert.True(status.HasCountdown);
        Assert.Equal(TimeSpan.FromHours(3), status.Countdown);
    }

    [Fact]
    public void ResolveStatus_BeforePremarket_UsesNextYahooWindow()
    {
        ExchangeTradingCalendar calendar = CreateCalendar(
            regular: Window("2026-05-20T13:30:00Z", "2026-05-20T20:00:00Z"),
            pre: Window("2026-05-20T09:00:00Z", "2026-05-20T13:30:00Z"),
            post: Window("2026-05-20T20:00:00Z", "2026-05-21T00:00:00Z"));

        ExchangeCalendarStatus status = _service.ResolveStatus(calendar, DateTimeOffset.Parse("2026-05-20T08:00:00Z"));

        Assert.Equal(MarketSession.Closed, status.Session);
        Assert.False(status.IsOpen);
        Assert.Equal(ExchangeCountdownTarget.Open, status.CountdownTo);
        Assert.True(status.HasCountdown);
        Assert.Equal(TimeSpan.FromHours(1), status.Countdown);
    }

    [Fact]
    public void ResolveStatus_WhenTimingUnavailable_ReturnsUnknownWithoutCountdown()
    {
        ExchangeTradingCalendar calendar = CreateCalendar(regular: null, pre: null, post: null);

        ExchangeCalendarStatus status = _service.ResolveStatus(calendar, DateTimeOffset.Parse("2026-05-20T15:00:00Z"));

        Assert.Equal(MarketSession.Unknown, status.Session);
        Assert.False(status.IsOpen);
        Assert.False(status.HasCountdown);
        Assert.Equal(ExchangeCountdownTarget.Unknown, status.CountdownTo);
        Assert.Equal("--", _service.FormatCompactStatus(status));
    }

    private static ExchangeTradingCalendar CreateCalendar(TradingPeriodWindow? regular, TradingPeriodWindow? pre, TradingPeriodWindow? post)
        => new()
        {
            CityKey = "NewYorkNasdaq",
            ExchangeCode = "NYSE",
            ExchangeName = "Nasdaq Composite",
            ExchangeSymbol = "^IXIC",
            TimeZoneId = "America/New_York",
            Source = "YFinance.NET chart metadata",
            CurrentTradingPeriod = new CurrentTradingPeriods(pre, regular, post)
        };

    private static TradingPeriodWindow Window(string startUtc, string endUtc)
        => new(DateTimeOffset.Parse(startUtc), DateTimeOffset.Parse(endUtc), "EDT", -14400);
}
