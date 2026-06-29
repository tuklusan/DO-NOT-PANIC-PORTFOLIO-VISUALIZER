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
using System.Diagnostics;

namespace YFinance.NET.Transport;

public sealed class RequestThrottle
{
    private readonly TimeSpan _minimumSpacing;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _lastRequestUtc = DateTimeOffset.MinValue;

    public RequestThrottle(TimeSpan minimumSpacing)
    {
        _minimumSpacing = minimumSpacing;
    }

    public async Task WaitAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            TimeSpan elapsed = DateTimeOffset.UtcNow - _lastRequestUtc;
            if (elapsed < _minimumSpacing)
            {
                await Task.Delay(_minimumSpacing - elapsed, cancellationToken).ConfigureAwait(false);
            }
            _lastRequestUtc = DateTimeOffset.UtcNow;
        }
        finally
        {
            _gate.Release();
        }
    }
}
