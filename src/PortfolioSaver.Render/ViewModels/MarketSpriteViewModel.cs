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
using System.Windows.Media;

namespace PortfolioSaver.Render.ViewModels;

public sealed class MarketSpriteViewModel : FloatingSpriteViewModel
{
    private string _spriteText = string.Empty;
    private Brush _foreground = Brushes.White;
    private double _scaleX = 1d;
    private double _baseY;
    private double _phase;
    private bool _isBag;
    private string _key = string.Empty;

    public string Key
    {
        get => _key;
        set => SetProperty(ref _key, value);
    }

    public string SpriteText
    {
        get => _spriteText;
        set => SetProperty(ref _spriteText, value);
    }

    public Brush Foreground
    {
        get => _foreground;
        set => SetProperty(ref _foreground, value);
    }

    public double ScaleX
    {
        get => _scaleX;
        set => SetProperty(ref _scaleX, value);
    }

    public double BaseY
    {
        get => _baseY;
        set => SetProperty(ref _baseY, value);
    }

    public double Phase
    {
        get => _phase;
        set => SetProperty(ref _phase, value);
    }

    public bool IsBag
    {
        get => _isBag;
        set => SetProperty(ref _isBag, value);
    }
}
