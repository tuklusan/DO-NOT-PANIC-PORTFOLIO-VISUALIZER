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
namespace PortfolioSaver.Shared.Helpers;

public static class TimeFormatHelper
{
    public static string ToAgeString(DateTimeOffset utcTime)
    {
        TimeSpan age = DateTimeOffset.UtcNow - utcTime;
        if (age.TotalSeconds < 60)
            return $"{Math.Max(0, (int)age.TotalSeconds)}s ago";
        if (age.TotalMinutes < 60)
            return $"{(int)age.TotalMinutes}m ago";
        return $"{(int)age.TotalHours}h ago";
    }
}
