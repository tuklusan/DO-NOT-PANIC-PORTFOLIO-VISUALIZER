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
using System.Windows.Threading;

namespace PortfolioSaver.Config.Services;

public sealed class BufferedSymbolValidationProgress
    : IProgress<YahooSymbolValidationProgress>, IDisposable
{
    private readonly object _gate = new();
    private readonly Dispatcher _dispatcher;
    private readonly TimeSpan _minimumUiInterval;
    private readonly Action<IReadOnlyList<YahooSymbolValidationProgress>> _reportBatch;
    private readonly List<YahooSymbolValidationProgress> _pending = [];
    private DateTimeOffset _nextUiReportUtc = DateTimeOffset.MinValue;
    private bool _dispatchPending;
    private bool _disposed;

    public BufferedSymbolValidationProgress(
        Dispatcher dispatcher,
        TimeSpan minimumUiInterval,
        Action<IReadOnlyList<YahooSymbolValidationProgress>> reportBatch)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(reportBatch);
        if (minimumUiInterval < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(minimumUiInterval), minimumUiInterval, "Progress interval must not be negative.");

        _dispatcher = dispatcher;
        _minimumUiInterval = minimumUiInterval;
        _reportBatch = reportBatch;
    }

    public void Report(YahooSymbolValidationProgress value)
    {
        bool shouldDispatch = false;
        lock (_gate)
        {
            if (_disposed)
                return;

            _pending.Add(value);
            DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
            if (!_dispatchPending && nowUtc >= _nextUiReportUtc)
            {
                _dispatchPending = true;
                _nextUiReportUtc = nowUtc + _minimumUiInterval;
                shouldDispatch = true;
            }
        }

        if (shouldDispatch)
            BeginProcessPending();
    }

    public void Flush()
    {
        if (IsDisposed())
            return;

        if (_dispatcher.CheckAccess())
        {
            ProcessPending();
            return;
        }

        try
        {
            _dispatcher.Invoke(ProcessPending, DispatcherPriority.Send);
        }
        catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException)
        {
        }
    }

    public void Dispose()
    {
        lock (_gate)
            _disposed = true;
    }

    private void BeginProcessPending()
    {
        if (IsDisposed())
            return;

        try
        {
            _dispatcher.BeginInvoke(ProcessPending, DispatcherPriority.Background);
        }
        catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException)
        {
        }
    }

    private bool IsDisposed()
    {
        lock (_gate)
            return _disposed;
    }

    private void ProcessPending()
    {
        List<YahooSymbolValidationProgress> batch;
        lock (_gate)
        {
            if (_disposed)
            {
                _dispatchPending = false;
                return;
            }

            if (_pending.Count == 0)
            {
                _dispatchPending = false;
                return;
            }

            batch = [.. _pending];
            _pending.Clear();
            _dispatchPending = false;
        }

        _reportBatch(batch);
    }
}
