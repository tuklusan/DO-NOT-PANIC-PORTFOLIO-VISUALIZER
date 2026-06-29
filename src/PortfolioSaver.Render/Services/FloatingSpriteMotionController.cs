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
