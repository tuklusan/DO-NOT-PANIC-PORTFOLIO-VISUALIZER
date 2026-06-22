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

public sealed class Ticker
{
    private static readonly string[] DefaultInfoModules = ["financialData", "quoteType", "defaultKeyStatistics", "assetProfile", "summaryDetail"];
    private readonly string _symbol;
    private readonly QuoteService _quoteService;
    private readonly QuoteSummaryService _quoteSummaryService;
    private readonly TickerInfoService _tickerInfoService;
    private readonly HistoryService _historyService;
    private readonly MarketTimingService _marketTimingService;

    internal Ticker(string symbol, QuoteService quoteService, QuoteSummaryService quoteSummaryService, TickerInfoService tickerInfoService, HistoryService historyService, MarketTimingService marketTimingService)
    {
        _symbol = symbol.Trim().ToUpperInvariant();
        _quoteService = quoteService;
        _quoteSummaryService = quoteSummaryService;
        _tickerInfoService = tickerInfoService;
        _historyService = historyService;
        _marketTimingService = marketTimingService;
    }

    public string Symbol => _symbol;

    public Task<QuoteSnapshot?> GetQuoteAsync(CancellationToken cancellationToken = default)
        => _quoteService.GetQuoteAsync(_symbol, cancellationToken);

    public Task<TickerInfo?> GetInfoAsync(CancellationToken cancellationToken = default)
        => _tickerInfoService.GetInfoAsync(_symbol, cancellationToken);

    public Task<QuoteSummaryResult?> GetSummaryAsync(CancellationToken cancellationToken = default)
        => _quoteSummaryService.GetSummaryAsync(_symbol, DefaultInfoModules, cancellationToken);

    public Task<IReadOnlyList<HistoricalBar>> GetHistoryAsync(DateTimeOffset startUtc, DateTimeOffset endUtc, string interval = "1d", CancellationToken cancellationToken = default)
        => _historyService.GetHistoryAsync(_symbol, startUtc, endUtc, interval, cancellationToken);

    public Task<HistoryResponse> GetHistoryResponseAsync(DateTimeOffset startUtc, DateTimeOffset endUtc, string interval = "1d", CancellationToken cancellationToken = default)
        => _historyService.GetHistoryResponseAsync(_symbol, startUtc, endUtc, interval, cancellationToken);

    public Task<MarketTimingSnapshot?> GetMarketTimingAsync(CancellationToken cancellationToken = default)
        => _marketTimingService.GetMarketTimingAsync(_symbol, cancellationToken);
}
