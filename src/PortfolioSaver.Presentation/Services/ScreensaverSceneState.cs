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
using System;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Render.ViewModels;

namespace PortfolioSaver.Screensaver.Services;

public sealed class ScreensaverSceneState
{
    public AppSettings Settings { get; init; } = new();
    public IReadOnlyDictionary<string, QuoteSnapshot> Quotes { get; init; } = new Dictionary<string, QuoteSnapshot>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<TapeViewModel> Tapes { get; init; } = [];
    public NewsFlasherViewModel News { get; init; } = new();
    public StatusBarViewModel Status { get; init; } = new();
    public IReadOnlyList<FloatingGraphViewModel> Graphs { get; init; } = [];
    public FloatingClockViewModel? Clock { get; init; }
    public IReadOnlyList<string> BackgroundPaths { get; init; } = [];
    public IReadOnlyDictionary<string, string> BackgroundAttributions { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public bool ShowNetworkWaitingOverlay { get; init; }
    public string? NetworkWaitingTitle { get; init; }
    public string? NetworkWaitingDetail { get; init; }
}
