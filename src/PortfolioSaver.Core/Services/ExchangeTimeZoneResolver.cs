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
namespace PortfolioSaver.Core.Services;

public static class ExchangeTimeZoneResolver
{
    public static TimeZoneInfo Resolve(string? timeZoneId)
    {
        if (TryFind(timeZoneId, out TimeZoneInfo? zone))
            return zone!;

        if (!string.IsNullOrWhiteSpace(timeZoneId) &&
            TimeZoneInfo.TryConvertIanaIdToWindowsId(timeZoneId, out string? windowsId) &&
            TryFind(windowsId, out zone))
        {
            return zone!;
        }

        if (!string.IsNullOrWhiteSpace(timeZoneId) &&
            TimeZoneInfo.TryConvertWindowsIdToIanaId(timeZoneId, out string? ianaId) &&
            TryFind(ianaId, out zone))
        {
            return zone!;
        }

        return TimeZoneInfo.Utc;
    }

    private static bool TryFind(string? timeZoneId, out TimeZoneInfo? zone)
    {
        zone = null;
        if (string.IsNullOrWhiteSpace(timeZoneId))
            return false;

        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }
}
