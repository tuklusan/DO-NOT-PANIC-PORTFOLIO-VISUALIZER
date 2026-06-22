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
using YFinance.NET.Config;
using YFinance.NET.Diagnostics;
using YFinance.NET.Features.History;
using YFinance.NET.Features.Quotes;
using YFinance.NET.Transport;

namespace YFinance.NET.Api;

public sealed class YFinanceClient : IDisposable
{
    // Keep the public composition surface close to upstream yfinance concepts so
    // future fork syncs have an obvious .NET landing zone.
    private readonly YahooFinanceHttpClient _httpClient;
    private readonly QuoteService _quoteService;
    private readonly QuoteSummaryService _quoteSummaryService;
    private readonly TickerInfoService _tickerInfoService;
    private readonly HistoryService _historyService;
    private readonly MarketTimingService _marketTimingService;
    private readonly YFinanceTrace _trace;

    public YFinanceClient(YFinanceOptions? options = null)
    {
        YFinanceOptions resolvedOptions = options ?? new YFinanceOptions();
        _trace = new YFinanceTrace(resolvedOptions.TraceSink);
        _httpClient = new YahooFinanceHttpClient(resolvedOptions, _trace);
        _quoteService = new QuoteService(_httpClient, resolvedOptions, _trace);
        _quoteSummaryService = new QuoteSummaryService(_httpClient, resolvedOptions, _trace);
        _tickerInfoService = new TickerInfoService(_quoteService, _quoteSummaryService, resolvedOptions, _trace);
        _historyService = new HistoryService(_httpClient, resolvedOptions, _trace);
        _marketTimingService = new MarketTimingService(_httpClient, resolvedOptions, _trace);
    }

    public Ticker Ticker(string symbol) => new(symbol, _quoteService, _quoteSummaryService, _tickerInfoService, _historyService, _marketTimingService);

    public Tickers Tickers(IEnumerable<string> symbols) => new(symbols, _quoteService, _quoteSummaryService, _tickerInfoService, _historyService, _marketTimingService);

    public void Dispose() => _httpClient.Dispose();
}
