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
    public static decimal? ComputeChangePercent(decimal? price, decimal? previousClose, decimal? change, decimal? reportedPercent)
    {
        if (previousClose.HasValue && previousClose.Value != 0m)
        {
            // Yahoo can occasionally publish price/previous-close pairs that do
            // not reconcile with its absolute change. Prefer the explicit change
            // because it is the field users see as the direction cue.
            if (change.HasValue)
                return (change.Value / previousClose.Value) * 100m;

            if (price.HasValue)
                return ((price.Value - previousClose.Value) / previousClose.Value) * 100m;
        }

        if (change.HasValue && reportedPercent.HasValue && change.Value != 0m && reportedPercent.Value != 0m && Math.Sign(change.Value) != Math.Sign(reportedPercent.Value))
            return Math.Abs(reportedPercent.Value) * (change.Value < 0m ? -1m : 1m);

        return reportedPercent;
    }
}
