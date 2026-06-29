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
