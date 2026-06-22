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
using System.Collections.Concurrent;
using PortfolioSaver.Core.Models;

namespace PortfolioSaver.Data.Services;

public static class RuntimeQuoteSeedStore
{
    private static readonly ConcurrentDictionary<string, QuoteSnapshot> Quotes = new(StringComparer.OrdinalIgnoreCase);

    public static void Publish(IEnumerable<QuoteSnapshot> quotes)
    {
        foreach (QuoteSnapshot quote in quotes.Where(static quote => !string.IsNullOrWhiteSpace(quote.Symbol)))
            Quotes[quote.Symbol] = Clone(quote);
    }

    public static IReadOnlyDictionary<string, QuoteSnapshot> ConsumeAll()
    {
        Dictionary<string, QuoteSnapshot> snapshot = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string symbol, QuoteSnapshot quote) in Quotes)
        {
            snapshot[symbol] = Clone(quote);
            Quotes.TryRemove(symbol, out _);
        }

        return snapshot;
    }

    internal static void Clear()
        => Quotes.Clear();

    private static QuoteSnapshot Clone(QuoteSnapshot source)
        => new()
        {
            Symbol = source.Symbol,
            Last = source.Last,
            Change = source.Change,
            ChangePercent = source.ChangePercent,
            PreviousClose = source.PreviousClose,
            Currency = source.Currency,
            MarketSession = source.MarketSession,
            ProviderTimestampUtc = source.ProviderTimestampUtc,
            FetchTimestampUtc = source.FetchTimestampUtc,
            IsStale = source.IsStale
        };
}
