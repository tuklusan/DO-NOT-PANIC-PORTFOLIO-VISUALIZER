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
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace PortfolioSaver.Media.Services;

public sealed class ImageTransitionController
{
    public void FadeTo(Image image, double durationSeconds = 1.2)
    {
        DoubleAnimation animation = new(0, 1, TimeSpan.FromSeconds(durationSeconds));
        image.BeginAnimation(UIElement.OpacityProperty, animation);
    }
}
