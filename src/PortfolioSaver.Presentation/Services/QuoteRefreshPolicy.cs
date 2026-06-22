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
using PortfolioSaver.Core.Constants;
using PortfolioSaver.Core.Enums;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Core.Services;
using PortfolioSaver.Shared.Diagnostics;

namespace PortfolioSaver.Screensaver.Services;

internal static class QuoteRefreshPolicy
{
    private static readonly TimeSpan UiSequentialCadence = TimeSpan.FromMilliseconds(1000);
    private static readonly TimeSpan MinimumHardStaleThreshold = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan HardStaleGrace = TimeSpan.FromMinutes(2);
    private static readonly Lazy<TimeZoneInfo?> NewYorkTimeZone = new(ResolveNewYorkTimeZone);

    // WARNING: freshness policy only. Do not use this value for UI scheduling; ordinary quote dispatch uses GetRefreshPollingInterval.
    public static TimeSpan GetConfiguredRefreshWindow(AppSettings settings, DateTimeOffset nowUtc)
    {
        int configuredSeconds = IsLikelyOpenMarket(nowUtc)
            ? settings.RefreshSecondsPortfolio
            : settings.RefreshSecondsOffHours;
        int clampedSeconds = Math.Clamp(configuredSeconds, Defaults.MinRefreshSeconds, Defaults.MaxRefreshSeconds);
        return TimeSpan.FromSeconds(clampedSeconds);
    }

    // Effective quote dispatch is intentionally fixed: the UI sends one symbol request per second.
    // Use GetConfiguredRefreshWindow for freshness/staleness policy, not scheduler cadence.
    public static TimeSpan GetEffectiveRefreshWindow(AppSettings settings, DateTimeOffset nowUtc)
        => GetRefreshPollingInterval(settings, nowUtc);

    public static TimeSpan GetRefreshPollingInterval(AppSettings settings, DateTimeOffset nowUtc)
        => UiSequentialCadence;

    public static TimeSpan GetHardStaleThreshold(AppSettings settings, DateTimeOffset nowUtc)
    {
        TimeSpan withGrace = GetEffectiveRefreshWindow(settings, nowUtc) + HardStaleGrace;
        return withGrace > MinimumHardStaleThreshold ? withGrace : MinimumHardStaleThreshold;
    }

    private static bool IsLikelyOpenMarket(DateTimeOffset nowUtc)
    {
        TimeZoneInfo? eastern = NewYorkTimeZone.Value;
        if (eastern is null)
            return false;

        DateTimeOffset easternNow = TimeZoneInfo.ConvertTime(nowUtc, eastern);
        if (easternNow.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            return false;

        // Known limitation: U.S. holidays and early-close days are not detected here. This policy only chooses a freshness window; quote truth still comes from YFinance.NET.
        // TimeOnly deliberately ignores DateTime.Kind; easternNow already carries the converted local clock time.
        TimeOnly localTime = TimeOnly.FromDateTime(easternNow.DateTime);
        return localTime >= new TimeOnly(9, 30) && localTime < new TimeOnly(16, 0);
    }

    private static TimeZoneInfo? ResolveNewYorkTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return ResolveIanaOrFallbackNewYorkTimeZone();
        }
        catch (InvalidTimeZoneException)
        {
            return ResolveIanaOrFallbackNewYorkTimeZone();
        }
    }

    private static TimeZoneInfo? ResolveIanaOrFallbackNewYorkTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        }
        catch (TimeZoneNotFoundException)
        {
            TraceLog.Warn("QuoteRefreshPolicy", "New York time zone unavailable; using off-hours refresh window.");
            return null;
        }
        catch (InvalidTimeZoneException)
        {
            TraceLog.Warn("QuoteRefreshPolicy", "New York time zone invalid; using off-hours refresh window.");
            return null;
        }
    }
}
