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
using PortfolioSaver.Shared.Services;
using System.Net.NetworkInformation;

namespace PortfolioSaver.Config.Services;

public sealed class ConfigConnectivityService
    : IConnectivityService, IDisposable
{
    private readonly InternetProbeService _probe = new();

    public ConfigConnectivityService()
    {
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
    }

    public event EventHandler? ConnectivityChanged;

    public bool IsInternetAvailable()
        => _probe.IsInternetAvailable();

    public Task<bool> IsInternetAvailableAsync(CancellationToken cancellationToken = default)
        => _probe.IsInternetAvailableAsync(cancellationToken);

    public void ForceProbe()
        => _probe.InvalidateCache();

    public void Dispose()
    {
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        ConnectivityChanged = null;
    }

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        _probe.InvalidateCache();
        ConnectivityChanged?.Invoke(this, EventArgs.Empty);
    }
}
