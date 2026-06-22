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
namespace PortfolioSaver.Screensaver.Services;

// This tracker is intentionally not thread-safe. The screensaver scene owns it
// from the WPF dispatcher thread, including timeout pruning and completions.
internal sealed class RuntimeQuoteInFlightTracker<TResult>
{
    private readonly Dictionary<string, RuntimeQuoteRequestState<TResult>> _requests;

    public RuntimeQuoteInFlightTracker(IEqualityComparer<string> comparer)
    {
        _requests = new Dictionary<string, RuntimeQuoteRequestState<TResult>>(comparer);
    }

    public int Count => _requests.Count;

    public bool Contains(string symbol)
    {
        return _requests.ContainsKey(symbol);
    }

    public void Add(string symbol, Task<TResult> task, DateTimeOffset startedAtUtc, CancellationTokenSource cancellation)
    {
        _requests[symbol] = new RuntimeQuoteRequestState<TResult>(task, startedAtUtc, cancellation);
    }

    public IReadOnlyList<RuntimeQuoteTimedOutRequest<TResult>> PruneStale(DateTimeOffset nowUtc, TimeSpan timeout)
    {
        List<RuntimeQuoteTimedOutRequest<TResult>> timedOut = [];
        foreach (string symbol in _requests
            .Where(pair => nowUtc - pair.Value.StartedAtUtc >= timeout)
            .Select(pair => pair.Key)
            .ToList())
        {
            RuntimeQuoteRequestState<TResult> state = _requests[symbol];
            _requests.Remove(symbol);
            TryCancel(state.Cancellation);
            state.Cancellation.Dispose();
            timedOut.Add(new RuntimeQuoteTimedOutRequest<TResult>(symbol, state, nowUtc - state.StartedAtUtc));
        }

        return timedOut;
    }

    public bool TryComplete(string symbol, Task<TResult> task, out RuntimeQuoteRequestState<TResult>? state)
    {
        if (!_requests.TryGetValue(symbol, out state) || !ReferenceEquals(state.Task, task))
            return false;

        _requests.Remove(symbol);
        state.Cancellation.Dispose();
        return true;
    }

    public void CancelAndClear()
    {
        foreach (RuntimeQuoteRequestState<TResult> state in _requests.Values)
        {
            TryCancel(state.Cancellation);
            state.Cancellation.Dispose();
        }

        _requests.Clear();
    }

    private static void TryCancel(CancellationTokenSource cancellation)
    {
        try
        {
            cancellation.Cancel();
        }
        catch
        {
        }
    }
}

internal sealed class RuntimeQuoteRequestState<TResult>(
    Task<TResult> task,
    DateTimeOffset startedAtUtc,
    CancellationTokenSource cancellation)
{
    public Task<TResult> Task { get; } = task;

    public DateTimeOffset StartedAtUtc { get; } = startedAtUtc;

    public CancellationTokenSource Cancellation { get; } = cancellation;
}

internal sealed class RuntimeQuoteTimedOutRequest<TResult>(
    string symbol,
    RuntimeQuoteRequestState<TResult> state,
    TimeSpan age)
{
    public string Symbol { get; } = symbol;

    public RuntimeQuoteRequestState<TResult> State { get; } = state;

    public TimeSpan Age { get; } = age;
}
