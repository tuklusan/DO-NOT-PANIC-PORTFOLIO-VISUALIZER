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
