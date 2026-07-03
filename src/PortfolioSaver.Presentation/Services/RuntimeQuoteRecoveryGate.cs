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
namespace PortfolioSaver.Presentation.Services;

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
