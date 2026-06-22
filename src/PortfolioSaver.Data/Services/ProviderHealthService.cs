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
using PortfolioSaver.Core.Models;

namespace PortfolioSaver.Data.Services;

public sealed class ProviderHealthService
{
    private readonly object _gate = new();
    private readonly ProviderHealthSnapshot _snapshot = new();

    /// <summary>
    /// Gets a point-in-time copy of the provider health state.
    /// </summary>
    /// <remarks>
    /// This service is thread-safe. The returned snapshot is a defensive copy; mutate it locally if needed,
    /// but call <see cref="MarkSuccess"/> or <see cref="MarkFailure"/> to update service state.
    /// </remarks>
    public ProviderHealthSnapshot Snapshot
    {
        get
        {
            lock (_gate)
                return CloneSnapshot(_snapshot);
        }
    }

    /// <summary>
    /// Marks the provider healthy and resets the consecutive-failure counter.
    /// </summary>
    public void MarkSuccess()
    {
        lock (_gate)
        {
            _snapshot.IsHealthy = true;
            _snapshot.StatusMessage = "OK";
            _snapshot.ConsecutiveFailures = 0;
            _snapshot.LastSuccessUtc = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// Marks the provider unhealthy and increments the consecutive-failure counter.
    /// </summary>
    public void MarkFailure(string message)
    {
        lock (_gate)
        {
            _snapshot.IsHealthy = false;
            _snapshot.StatusMessage = string.IsNullOrWhiteSpace(message) ? "Unknown failure" : message;
            _snapshot.ConsecutiveFailures++;
            _snapshot.LastFailureUtc = DateTimeOffset.UtcNow;
        }
    }

    // Keep this explicit so new ProviderHealthSnapshot fields must be consciously copied.
    private static ProviderHealthSnapshot CloneSnapshot(ProviderHealthSnapshot snapshot)
        => new()
        {
            IsHealthy = snapshot.IsHealthy,
            StatusMessage = snapshot.StatusMessage,
            ConsecutiveFailures = snapshot.ConsecutiveFailures,
            LastSuccessUtc = snapshot.LastSuccessUtc,
            LastFailureUtc = snapshot.LastFailureUtc
        };
}
