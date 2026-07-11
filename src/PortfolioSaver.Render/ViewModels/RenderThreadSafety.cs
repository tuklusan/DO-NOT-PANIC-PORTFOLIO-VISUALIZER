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

namespace PortfolioSaver.Render.ViewModels;

internal static class RenderThreadSafety
{
    public static Brush FreezeBrush(Brush? brush, Brush fallback)
    {
        brush ??= fallback;
        if (brush.IsFrozen)
            return brush;

        Brush clone = brush.CloneCurrentValue();
        if (!clone.CanFreeze)
            throw new InvalidOperationException("Render view-model brushes must be freezable before crossing WPF dispatcher boundaries.");

        clone.Freeze();
        return clone;
    }

    public static PointCollection FreezePoints(IEnumerable<Point>? points)
    {
        if (points is PointCollection { IsFrozen: true } frozen)
            return frozen;

        PointCollection collection = points is null ? [] : new PointCollection(points);
        if (collection.CanFreeze)
            collection.Freeze();

        return collection;
    }

    public static IReadOnlyList<PointCollection> FreezePointSegments(IEnumerable<PointCollection>? segments)
    {
        List<PointCollection> frozen = [];
        if (segments is null)
            return frozen.AsReadOnly();

        foreach (PointCollection segment in segments)
            frozen.Add(segment.IsFrozen ? segment : FreezePoints(segment));

        return frozen.AsReadOnly();
    }
}
