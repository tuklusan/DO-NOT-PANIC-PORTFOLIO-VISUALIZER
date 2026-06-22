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

namespace PortfolioSaver.Data.Services;

public static class YFinanceSymbolMapper
{
    private static readonly IReadOnlyDictionary<string, string> RequestAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["INDY.US"] = "INDY",
        ["EWA.US"] = "EWA",
        ["US10Y"] = "^TNX",
        ["US2M"] = "^IRX"
    };

    public static string Normalize(string? symbol)
        => (symbol ?? string.Empty).Trim().ToUpperInvariant();

    public static string ToRequestSymbol(string? symbol)
    {
        string normalized = Normalize(symbol);
        return RequestAliases.TryGetValue(normalized, out string? mapped) ? mapped : normalized;
    }

    public static string ToResponseMatchKey(string? symbol)
        => Normalize(symbol).TrimStart('^');

    public static decimal? NormalizeNumericValue(string requestedSymbol, decimal? value)
    {
        if (!value.HasValue)
            return null;

        string normalized = Normalize(requestedSymbol);
        if (normalized is "US10Y" or "US2M")
            return Math.Round(value.Value / 10m, 3);

        return value;
    }

    public static MarketSession MapMarketSession(string? marketState)
    {
        if (string.IsNullOrWhiteSpace(marketState))
            return MarketSession.Unknown;

        return marketState.Trim().ToUpperInvariant() switch
        {
            "PRE" or "PREPRE" or "PREPREMARKET" or "PREMARKET" => MarketSession.PreMarket,
            "REGULAR" or "OPEN" => MarketSession.Regular,
            "POST" or "POSTPOST" or "POSTMARKET" or "AFTER_HOURS" => MarketSession.AfterHours,
            "CLOSED" => MarketSession.Closed,
            _ => MarketSession.Unknown
        };
    }
}
