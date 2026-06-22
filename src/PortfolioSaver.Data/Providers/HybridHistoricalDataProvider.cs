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
using PortfolioSaver.Data.Interfaces;
using PortfolioSaver.Data.Services;
using PortfolioSaver.Shared.Diagnostics;
using YFinance.NET.Protocol.Dtos;

namespace PortfolioSaver.Data.Providers;

public sealed class HybridHistoricalDataProvider : IHistoricalDataProvider
{
    private const int MaxConcurrentHistoryRequests = 2;
    private const int MaxHistoryFetchAttempts = 2;
    private readonly IHistoricalCacheService _cacheService;
    private readonly TimeSpan _cacheFreshness;
    private readonly HistoryFetchAsync _historyFetchAsync;

    public HybridHistoricalDataProvider(
        IHistoricalCacheService cacheService,
        TimeSpan? cacheFreshness = null)
    {
        _cacheService = cacheService;
        _cacheFreshness = cacheFreshness ?? TimeSpan.FromHours(12);
        _historyFetchAsync = FetchHistoryFromYFinanceAsync;
    }

    [Obsolete("Use HybridHistoricalDataProvider(IHistoricalCacheService, TimeSpan?) instead. Extra legacy parameters were unused and are ignored.")]
    public HybridHistoricalDataProvider(
        IHistoricalCacheService cacheService,
        HttpClient? httpClient,
        TimeSpan? cacheFreshness,
        int rotationSeed = 0,
        IReadOnlyDictionary<string, SymbolProfile>? symbolProfiles = null)
        : this(cacheService, cacheFreshness)
    {
        _ = httpClient;
        _ = rotationSeed;
        _ = symbolProfiles;
    }


    internal HybridHistoricalDataProvider(
        IHistoricalCacheService cacheService,
        HistoryFetchAsync historyFetchAsync,
        TimeSpan? cacheFreshness = null)
    {
        _cacheService = cacheService;
        _historyFetchAsync = historyFetchAsync;
        _cacheFreshness = cacheFreshness ?? TimeSpan.FromHours(12);
    }

    public async Task<IReadOnlyList<TickerHistorySnapshot>> GetHistoryAsync(
        IEnumerable<string> symbols,
        int lookbackDays,
        CancellationToken cancellationToken = default)
    {
        List<string> orderedSymbols = symbols
            .Select(YFinanceSymbolMapper.Normalize)
            .Where(static symbol => !string.IsNullOrWhiteSpace(symbol))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (orderedSymbols.Count == 0)
            return [];

        await _cacheService.PurgeExpiredAsync(cancellationToken).ConfigureAwait(false);

        ConcurrentDictionary<string, TickerHistorySnapshot> resolved = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, TickerHistorySnapshot> staleCache = new(StringComparer.OrdinalIgnoreCase);
        List<string> pending = [];

        foreach (string symbol in orderedSymbols)
        {
            TickerHistorySnapshot? cached = await _cacheService.LoadAsync(symbol, cancellationToken).ConfigureAwait(false);
            if (cached is not null && cached.LookbackDays == lookbackDays && cached.IsFresh(_cacheFreshness))
            {
                TraceLog.InfoState(
                    "YFinanceUiBridge",
                    "HistoryRequestServedFromCache",
                    [new("symbol", symbol), new("lookback_days", lookbackDays), new("fetch_timestamp_utc", cached.FetchTimestampUtc)]);
                resolved[symbol] = cached;
                continue;
            }

            if (cached is not null)
                staleCache[symbol] = cached;

            pending.Add(symbol);
        }

        if (pending.Count > 0)
        {
            DateTimeOffset endUtc = DateTimeOffset.UtcNow;
            DateTimeOffset startUtc = endUtc.AddDays(-Math.Max(1, lookbackDays));

            using SemaphoreSlim historyGate = new(MaxConcurrentHistoryRequests, MaxConcurrentHistoryRequests);
            Task[] fetchTasks = pending
                .Select(symbol => FetchAndCacheHistoryAsync(symbol, lookbackDays, startUtc, endUtc, historyGate, resolved, cancellationToken))
                .ToArray();
            await Task.WhenAll(fetchTasks).ConfigureAwait(false);
        }

        List<TickerHistorySnapshot> results = [];
        foreach (string symbol in orderedSymbols)
        {
            if (resolved.TryGetValue(symbol, out TickerHistorySnapshot? fetched))
            {
                results.Add(fetched);
                continue;
            }

            if (staleCache.TryGetValue(symbol, out TickerHistorySnapshot? cached))
            {
                TraceLog.InfoState(
                    "YFinanceUiBridge",
                    "HistoryRequestFellBackToStaleCache",
                    [new("symbol", symbol), new("lookback_days", lookbackDays), new("fetch_timestamp_utc", cached.FetchTimestampUtc)]);
                results.Add(cached);
                continue;
            }

            results.Add(new TickerHistorySnapshot
            {
                Symbol = symbol,
                LookbackDays = lookbackDays,
                FetchTimestampUtc = DateTimeOffset.UtcNow,
                Points = []
            });
        }

        TraceLog.InfoState(
            "YFinanceNetHistoricalProvider",
            "HistoryBatchComplete",
            [new("requested_count", orderedSymbols.Count), new("resolved_count", resolved.Count), new("lookback_days", lookbackDays)]);

        return results;
    }

    private async Task FetchAndCacheHistoryAsync(
        string symbol,
        int lookbackDays,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        SemaphoreSlim historyGate,
        ConcurrentDictionary<string, TickerHistorySnapshot> resolved,
        CancellationToken cancellationToken)
    {
        bool acquired = false;
        string operationId = YFinanceRuntimeClientFactory.CreateOperationId("history");
        try
        {
            await historyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            acquired = true;
            string requestSymbol = YFinanceSymbolMapper.ToRequestSymbol(symbol);
            TraceLog.InfoState(
                "YFinanceUiBridge",
                "HistoryRequestStart",
                [new("operation_id", operationId), new("symbol", symbol), new("request_symbol", requestSymbol), new("lookback_days", lookbackDays)]);
            HistoryResponseDto response = await FetchHistoryWithRetryAsync(
                    requestSymbol,
                    startUtc,
                    endUtc,
                    ResolveInterval(lookbackDays),
                    operationId,
                    cancellationToken)
                .ConfigureAwait(false);

            TickerHistorySnapshot snapshot = MapHistory(symbol, lookbackDays, response);
            if (snapshot.Points.Count > 0)
            {
                resolved[symbol] = snapshot;
                await _cacheService.SaveAsync(snapshot, cancellationToken).ConfigureAwait(false);
            }

            TraceLog.InfoState(
                "YFinanceUiBridge",
                "HistoryRequestComplete",
                [new("operation_id", operationId), new("symbol", symbol), new("point_count", snapshot.Points.Count), new("metadata_timezone", response.Metadata?.ExchangeTimezoneName)]);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TraceLog.InfoState(
                "YFinanceUiBridge",
                "HistoryFetchCanceled",
                [new("operation_id", operationId), new("symbol", symbol), new("lookback_days", lookbackDays)]);
            throw;
        }
        catch (Exception ex)
        {
            TraceLog.WarnState(
                "YFinanceUiBridge",
                "HistoryRequestFailed",
                [new("operation_id", operationId), new("symbol", symbol), new("lookback_days", lookbackDays), new("message", ex.Message)]);
            TraceLog.WarnState(
                "YFinanceNetHistoricalProvider",
                "HistoryFetchFailed",
                [new("symbol", symbol), new("lookback_days", lookbackDays), new("message", ex.Message)]);
        }
        finally
        {
            if (acquired)
                historyGate.Release();
        }
    }

    private async Task<HistoryResponseDto> FetchHistoryWithRetryAsync(
        string requestSymbol,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        string interval,
        string operationId,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;
        for (int attempt = 1; attempt <= MaxHistoryFetchAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                string attemptOperationId = attempt == 1
                    ? operationId
                    : YFinanceRuntimeClientFactory.CreateOperationId("history-retry");
                return await _historyFetchAsync(
                        requestSymbol,
                        startUtc,
                        endUtc,
                        interval,
                        attemptOperationId,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < MaxHistoryFetchAttempts && IsTransientHistoryException(ex))
            {
                lastException = ex;
                await Task.Delay(TimeSpan.FromMilliseconds(300 * attempt), cancellationToken).ConfigureAwait(false);
            }
        }

        throw lastException ?? new InvalidOperationException("History fetch retry failed without a captured exception.");
    }

    private static bool IsTransientHistoryException(Exception ex)
        => ex is HttpRequestException or IOException or TimeoutException;

    private static async Task<HistoryResponseDto> FetchHistoryFromYFinanceAsync(
        string requestSymbol,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        string interval,
        string operationId,
        CancellationToken cancellationToken)
        => await YFinanceRuntimeClientFactory
            .RunSerializedAsync(
                "history",
                operationId,
                (client, token) => client.GetHistoryAsync(requestSymbol, startUtc, endUtc, interval, token),
                cancellationToken)
            .ConfigureAwait(false);

    private static TickerHistorySnapshot MapHistory(string originalSymbol, int lookbackDays, HistoryResponseDto response)
    {
        return new TickerHistorySnapshot
        {
            Symbol = originalSymbol,
            LookbackDays = lookbackDays,
            FetchTimestampUtc = DateTimeOffset.UtcNow,
            Points = response.Bars
                .Where(static bar => bar.Close.HasValue)
                .Select(bar => new HistoricalPricePoint
                {
                    TimestampUtc = bar.TimestampUtc,
                    Close = YFinanceSymbolMapper.NormalizeNumericValue(originalSymbol, bar.Close) ?? 0m
                })
                .Where(static point => point.Close > 0m)
                .OrderBy(point => point.TimestampUtc)
                .ToList()
        };
    }

    private static string ResolveInterval(int lookbackDays)
        => lookbackDays <= 1 ? "1h" : "1d";

    internal delegate Task<HistoryResponseDto> HistoryFetchAsync(
        string requestSymbol,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        string interval,
        string operationId,
        CancellationToken cancellationToken);
}
