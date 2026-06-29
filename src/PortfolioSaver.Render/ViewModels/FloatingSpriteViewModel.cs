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
using PortfolioSaver.Shared.Infrastructure;

namespace PortfolioSaver.Render.ViewModels;

public abstract class FloatingSpriteViewModel : BindableBase
{
    private double _x;
    private double _y;
    private double _velocityX;
    private double _velocityY;
    private double _width;
    private double _height;
    private bool _bounceWithinViewport = true;

    public double X
    {
        get => _x;
        set => SetProperty(ref _x, value);
    }

    public double Y
    {
        get => _y;
        set => SetProperty(ref _y, value);
    }

    public double VelocityX
    {
        get => _velocityX;
        set => SetProperty(ref _velocityX, value);
    }

    public double VelocityY
    {
        get => _velocityY;
        set => SetProperty(ref _velocityY, value);
    }

    public double Width
    {
        get => _width;
        set => SetProperty(ref _width, value);
    }

    public double Height
    {
        get => _height;
        set => SetProperty(ref _height, value);
    }

    public bool BounceWithinViewport
    {
        get => _bounceWithinViewport;
        set => SetProperty(ref _bounceWithinViewport, value);
    }
}
