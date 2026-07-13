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
namespace YFinance.NET.Models;

internal static class QuoteMath
{
    public static decimal? ComputeChange(decimal? price, decimal? previousClose, decimal? reportedChange)
    {
        // Yahoo can transiently report change == -previousClose while retaining
        // a positive price. Normalize only that impossible total-loss tuple.
        if (price is > 0m && previousClose is > 0m && reportedChange is decimal reported && reported <= -previousClose.Value)
            return price.Value - previousClose.Value;

        return reportedChange;
    }

    public static decimal? ComputeChangePercent(decimal? price, decimal? previousClose, decimal? change, decimal? reportedPercent)
    {
        decimal? normalizedChange = ComputeChange(price, previousClose, change);
        if (previousClose.HasValue && previousClose.Value != 0m)
        {
            // Yahoo can occasionally publish price/previous-close pairs that do
            // not reconcile with its absolute change. Prefer the explicit change
            // because it is the field users see as the direction cue.
            if (normalizedChange.HasValue)
                return (normalizedChange.Value / previousClose.Value) * 100m;

            if (price.HasValue)
                return ((price.Value - previousClose.Value) / previousClose.Value) * 100m;
        }

        if (normalizedChange.HasValue && reportedPercent.HasValue && normalizedChange.Value != 0m && reportedPercent.Value != 0m && Math.Sign(normalizedChange.Value) != Math.Sign(reportedPercent.Value))
            return Math.Abs(reportedPercent.Value) * (normalizedChange.Value < 0m ? -1m : 1m);

        return reportedPercent;
    }
}
