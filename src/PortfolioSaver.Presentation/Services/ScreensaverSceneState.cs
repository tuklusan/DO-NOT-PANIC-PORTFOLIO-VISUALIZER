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
