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
namespace PortfolioSaver.Data.Services;

public sealed class RateLimitGuard : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _lastRunUtc = DateTimeOffset.MinValue;

    // Test hook for cancellation regression coverage; production code should not make decisions from semaphore internals.
    internal int CurrentCountForTests => _gate.CurrentCount;

    /// <summary>
    /// Serializes callers so a shared guard enforces one completed lookup interval at a time.
    /// </summary>
    /// <remarks>
    /// This guard is intentionally strict and is not reentrant; recursive calls on the same instance will deadlock.
    /// </remarks>
    public async Task WaitIfNeededAsync(TimeSpan minimumInterval, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            TimeSpan elapsed = DateTimeOffset.UtcNow - _lastRunUtc;
            if (elapsed < minimumInterval)
                await Task.Delay(minimumInterval - elapsed, cancellationToken).ConfigureAwait(false);

            _lastRunUtc = DateTimeOffset.UtcNow;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _gate.Dispose();
    }
}
