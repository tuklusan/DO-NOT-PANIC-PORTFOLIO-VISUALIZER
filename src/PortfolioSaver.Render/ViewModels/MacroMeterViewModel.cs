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
using System.Windows;
using System.Windows.Media;
using PortfolioSaver.Shared.Infrastructure;

namespace PortfolioSaver.Render.ViewModels;

public sealed class MacroMeterViewModel : BindableBase
{
    private string _label = string.Empty;
    private string _valueText = "--";
    private string _changeText = "--";
    private Brush _accentBrush = Brushes.SlateGray;
    private string _trackPath = BuildArcPath(1d);
    private string _arcPath = string.Empty;
    private string _needlePath = string.Empty;

    public string Label
    {
        get => _label;
        set => SetProperty(ref _label, value);
    }

    public string ValueText
    {
        get => _valueText;
        set => SetProperty(ref _valueText, value);
    }

    public string ChangeText
    {
        get => _changeText;
        set => SetProperty(ref _changeText, value);
    }

    public Brush AccentBrush
    {
        get => _accentBrush;
        set => SetProperty(ref _accentBrush, value);
    }

    public string ArcPath
    {
        get => _arcPath;
        private set => SetProperty(ref _arcPath, value);
    }

    public string TrackPath
    {
        get => _trackPath;
        private set => SetProperty(ref _trackPath, value);
    }

    public string NeedlePath
    {
        get => _needlePath;
        private set => SetProperty(ref _needlePath, value);
    }

    public void SetFill(double normalizedFill)
    {
        double fill = Math.Clamp(normalizedFill, 0d, 1d);
        TrackPath = BuildArcPath(1d);
        ArcPath = BuildArcPath(fill);
        NeedlePath = BuildNeedlePath(fill);
    }

    private static string BuildArcPath(double fill)
    {
        const double radius = 10d;
        const double centerX = 12d;
        const double centerY = 12d;
        const double startDegrees = 210d;
        const double maxSweepDegrees = 240d;
        double sweepDegrees = Math.Max(2d, maxSweepDegrees * fill);
        double endDegrees = startDegrees + sweepDegrees;

        Point start = PolarToCartesian(centerX, centerY, radius, startDegrees);
        Point end = PolarToCartesian(centerX, centerY, radius, endDegrees);
        int largeArc = sweepDegrees > 180d ? 1 : 0;
        return $"M {start.X:0.###},{start.Y:0.###} A {radius:0.###},{radius:0.###} 0 {largeArc} 1 {end.X:0.###},{end.Y:0.###}";
    }

    private static string BuildNeedlePath(double fill)
    {
        const double centerX = 12d;
        const double centerY = 12d;
        const double radius = 8d;
        const double startDegrees = 210d;
        const double maxSweepDegrees = 240d;
        double angle = startDegrees + (maxSweepDegrees * fill);
        Point needleTip = PolarToCartesian(centerX, centerY, radius, angle);
        return $"M {centerX:0.###},{centerY:0.###} L {needleTip.X:0.###},{needleTip.Y:0.###}";
    }

    private static Point PolarToCartesian(double cx, double cy, double radius, double angleDegrees)
    {
        double radians = angleDegrees * (Math.PI / 180d);
        return new Point(
            cx + radius * Math.Cos(radians),
            cy + radius * Math.Sin(radians));
    }
}
