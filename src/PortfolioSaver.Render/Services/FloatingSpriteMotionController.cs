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
using System.Windows;
using PortfolioSaver.Render.ViewModels;

namespace PortfolioSaver.Render.Services;

public sealed class FloatingSpriteMotionController
{
    public void Step(FloatingSpriteViewModel sprite, Rect bounds, double elapsedSeconds)
    {
        sprite.X += sprite.VelocityX * elapsedSeconds;
        sprite.Y += sprite.VelocityY * elapsedSeconds;

        if (!sprite.BounceWithinViewport)
            return;

        double minX = bounds.Left;
        double maxX = Math.Max(bounds.Left, bounds.Right - sprite.Width);
        double minY = bounds.Top;
        double maxY = Math.Max(bounds.Top, bounds.Bottom - sprite.Height);

        if (sprite.X <= minX)
        {
            sprite.X = minX;
            sprite.VelocityX = Math.Abs(sprite.VelocityX);
        }
        else if (sprite.X >= maxX)
        {
            sprite.X = maxX;
            sprite.VelocityX = -Math.Abs(sprite.VelocityX);
        }

        if (sprite.Y <= minY)
        {
            sprite.Y = minY;
            sprite.VelocityY = Math.Abs(sprite.VelocityY);
        }
        else if (sprite.Y >= maxY)
        {
            sprite.Y = maxY;
            sprite.VelocityY = -Math.Abs(sprite.VelocityY);
        }
    }
}
