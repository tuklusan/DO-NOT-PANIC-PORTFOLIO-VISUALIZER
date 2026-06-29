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
using PortfolioSaver.Core.Models;

namespace PortfolioSaver.Render.Services;

public sealed class TickerFormatter
{
    public string Format(QuoteSnapshot quote, TickerItem? ticker = null)
    {
        string price = quote.Last?.ToString("0.##") ?? "--";
        string pct = quote.ChangePercent is decimal p ? $"{p:+0.##;-0.##;0}%" : "--";

        if (ticker?.Quantity is decimal quantity && quote.Last is decimal last)
        {
            decimal marketValue = quantity * last;
            return $"{quote.Symbol} {price} {pct} MV ${marketValue:N0}";
        }

        return $"{quote.Symbol} {price} {pct}";
    }
}
