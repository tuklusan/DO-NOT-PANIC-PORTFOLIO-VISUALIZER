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
using PortfolioSaver.Core.Models;
using PortfolioSaver.Render.ViewModels;

namespace PortfolioSaver.Render.Services;

public sealed class HistoricalGraphBuilder
{
    public FloatingGraphViewModel Build(string tapeName, TickerHistorySnapshot snapshot, Size targetSize)
    {
        FloatingGraphViewModel vm = new()
        {
            Symbol = snapshot.Symbol,
            TapeName = tapeName,
            OverlayText = $"{tapeName} | {snapshot.LookbackDays}D",
            Width = Math.Max(1, targetSize.Width),
            Height = Math.Max(1, targetSize.Height),
            PlotWidth = Math.Max(1, targetSize.Width),
            PlotHeight = Math.Max(1, targetSize.Height)
        };

        if (snapshot.Points.Count == 0)
            return vm;

        decimal min = snapshot.Points.Min(p => p.Close);
        decimal max = snapshot.Points.Max(p => p.Close);
        decimal range = max - min;
        if (range <= 0)
            range = 1;

        double width = vm.PlotWidth;
        double height = vm.PlotHeight;

        PointCollection green = [];
        PointCollection red = [];
        List<PointCollection> greenSegments = [];
        List<PointCollection> redSegments = [];

        for (int i = 0; i < snapshot.Points.Count; i++)
        {
            HistoricalPricePoint point = snapshot.Points[i];
            double x = snapshot.Points.Count == 1 ? width / 2 : width * i / (snapshot.Points.Count - 1d);
            double normalized = (double)((point.Close - min) / range);
            double y = height - (normalized * height);

            bool isGainStart = i == 0 || point.Close >= snapshot.Points[i - 1].Close;
            GraphPointViewModel graphPoint = new() { X = x, Y = y, IsGainSegmentStart = isGainStart };
            vm.Points.Add(graphPoint);

            Point p = new(x, y);
            if (isGainStart)
            {
                green.Add(p);
                red.Add(new Point(double.NaN, double.NaN));
            }
            else
            {
                red.Add(p);
                green.Add(new Point(double.NaN, double.NaN));
            }

            if (i == 0)
                continue;

            Point previous = new(vm.Points[i - 1].X, vm.Points[i - 1].Y);
            PointCollection segment = [previous, p];
            segment.Freeze();
            if (point.Close >= snapshot.Points[i - 1].Close)
                greenSegments.Add(segment);
            else
                redSegments.Add(segment);
        }

        green.Freeze();
        red.Freeze();
        vm.GreenPoints = green;
        vm.RedPoints = red;
        vm.GreenSegments = greenSegments;
        vm.RedSegments = redSegments;
        if (vm.Points.Count >= 2)
        {
            vm.LatestSegmentPoints =
            [
                new Point(vm.Points[^2].X, vm.Points[^2].Y),
                new Point(vm.Points[^1].X, vm.Points[^1].Y)
            ];
            vm.LatestSegmentPoints.Freeze();
        }
        vm.MaxScaleText = FormatScaleValue(max);
        vm.MidScaleText = FormatScaleValue((min + max) / 2m);
        vm.MinScaleText = FormatScaleValue(min);
        vm.LeftTimeScaleText = FormatTimeScale(snapshot.Points.First().TimestampUtc, snapshot.LookbackDays);
        vm.MiddleTimeScaleText = FormatTimeScale(snapshot.Points[snapshot.Points.Count / 2].TimestampUtc, snapshot.LookbackDays);
        vm.RightTimeScaleText = FormatTimeScale(snapshot.Points.Last().TimestampUtc, snapshot.LookbackDays);
        decimal latestClose = snapshot.Points.Last().Close;
        vm.LastText = latestClose.ToString("0.00");
        if (snapshot.Points.Count >= 2)
        {
            decimal previousClose = snapshot.Points[^2].Close;
            decimal changePercent = previousClose == 0m ? 0m : ((latestClose - previousClose) / previousClose) * 100m;
            vm.ChangeText = $"{(changePercent >= 0 ? "+" : string.Empty)}{changePercent:0.00}%";
            vm.ChangeForeground = changePercent switch
            {
                > 0m => Brushes.LimeGreen,
                < 0m => Brushes.OrangeRed,
                _ => Brushes.Gainsboro
            };
            vm.LatestSegmentBrush = vm.ChangeForeground;
        }
        return vm;
    }

    private static string FormatScaleValue(decimal value)
    {
        decimal magnitude = Math.Abs(value);
        if (magnitude >= 1000m)
            return value.ToString("0");

        if (magnitude >= 100m)
            return value.ToString("0.0");

        return value.ToString("0.00");
    }

    private static string FormatTimeScale(DateTimeOffset pointInTime, int lookbackDays)
        => lookbackDays <= 1
            ? pointInTime.ToLocalTime().ToString("HH:mm")
            : pointInTime.ToLocalTime().ToString("M/d");
}
