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
using YFinance.NET.Features.History;
using YFinance.NET.Features.Quotes;
using YFinance.NET.Models;

namespace YFinance.NET.Api;

public sealed class Tickers
{
    private static readonly string[] DefaultInfoModules = ["financialData", "quoteType", "defaultKeyStatistics", "assetProfile", "summaryDetail"];

    private readonly string[] _symbols;
    private readonly QuoteService _quoteService;
    private readonly QuoteSummaryService _quoteSummaryService;
    private readonly TickerInfoService _tickerInfoService;
    private readonly HistoryService _historyService;
    private readonly MarketTimingService _marketTimingService;

    internal Tickers(IEnumerable<string> symbols, QuoteService quoteService, QuoteSummaryService quoteSummaryService, TickerInfoService tickerInfoService, HistoryService historyService, MarketTimingService marketTimingService)
    {
        _symbols = symbols.Select(static symbol => symbol.Trim().ToUpperInvariant())
                          .Where(static symbol => !string.IsNullOrWhiteSpace(symbol))
                          .Distinct(StringComparer.Ordinal)
                          .ToArray();
        _quoteService = quoteService;
        _quoteSummaryService = quoteSummaryService;
        _tickerInfoService = tickerInfoService;
        _historyService = historyService;
        _marketTimingService = marketTimingService;
    }

    public IReadOnlyList<string> Symbols => _symbols;

    public IReadOnlyDictionary<string, Ticker> AsDictionary()
        => _symbols.ToDictionary(static symbol => symbol, symbol => new Ticker(symbol, _quoteService, _quoteSummaryService, _tickerInfoService, _historyService, _marketTimingService), StringComparer.Ordinal);

    public Task<IReadOnlyDictionary<string, QuoteSnapshot>> GetQuotesAsync(CancellationToken cancellationToken = default)
        => _quoteService.GetQuotesAsync(_symbols, cancellationToken);

    public Task<IReadOnlyDictionary<string, TickerInfo?>> GetInfosAsync(CancellationToken cancellationToken = default)
        => _tickerInfoService.GetInfosAsync(_symbols, cancellationToken);

    public async Task<IReadOnlyDictionary<string, QuoteSummaryResult?>> GetSummariesAsync(CancellationToken cancellationToken = default)
    {
        Dictionary<string, QuoteSummaryResult?> results = new(StringComparer.OrdinalIgnoreCase);
        foreach (string symbol in _symbols)
        {
            results[symbol] = await _quoteSummaryService.GetSummaryAsync(symbol, DefaultInfoModules, cancellationToken).ConfigureAwait(false);
        }

        return results;
    }

    public async Task<IReadOnlyDictionary<string, HistoryResponse>> GetHistoryResponsesAsync(DateTimeOffset startUtc, DateTimeOffset endUtc, string interval = "1d", CancellationToken cancellationToken = default)
    {
        Dictionary<string, HistoryResponse> results = new(StringComparer.OrdinalIgnoreCase);
        foreach (string symbol in _symbols)
        {
            results[symbol] = await _historyService.GetHistoryResponseAsync(symbol, startUtc, endUtc, interval, cancellationToken).ConfigureAwait(false);
        }

        return results;
    }

    public async Task<IReadOnlyDictionary<string, MarketTimingSnapshot?>> GetMarketTimingsAsync(CancellationToken cancellationToken = default)
    {
        Dictionary<string, MarketTimingSnapshot?> results = new(StringComparer.OrdinalIgnoreCase);
        foreach (string symbol in _symbols)
        {
            results[symbol] = await _marketTimingService.GetMarketTimingAsync(symbol, cancellationToken).ConfigureAwait(false);
        }

        return results;
    }
}
