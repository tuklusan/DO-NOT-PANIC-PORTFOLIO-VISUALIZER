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

internal sealed class RuntimeQuoteRecoveryGate
{
    private readonly int _failureThreshold;
    private readonly TimeSpan _cooldown;
    private int _resetInProgress;
    private long _lastSuccessfulResetUnixMilliseconds = long.MinValue;

    public RuntimeQuoteRecoveryGate(int failureThreshold, TimeSpan cooldown)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(failureThreshold);
        _failureThreshold = failureThreshold;
        _cooldown = cooldown;
    }

    public bool TryEnter(int failureStreak, DateTimeOffset nowUtc)
    {
        if (failureStreak < _failureThreshold)
            return false;

        long lastResetUnixMilliseconds = Interlocked.Read(ref _lastSuccessfulResetUnixMilliseconds);
        long nowUnixMilliseconds = nowUtc.ToUnixTimeMilliseconds();
        if (lastResetUnixMilliseconds != long.MinValue &&
            nowUnixMilliseconds - lastResetUnixMilliseconds < _cooldown.TotalMilliseconds)
        {
            return false;
        }

        return Interlocked.CompareExchange(ref _resetInProgress, 1, 0) == 0;
    }

    public void MarkResetSucceeded(DateTimeOffset nowUtc)
        => Interlocked.Exchange(ref _lastSuccessfulResetUnixMilliseconds, nowUtc.ToUnixTimeMilliseconds());

    public void Exit()
        => Interlocked.Exchange(ref _resetInProgress, 0);
}
