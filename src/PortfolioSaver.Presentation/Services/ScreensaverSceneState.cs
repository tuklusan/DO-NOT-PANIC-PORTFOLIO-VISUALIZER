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
