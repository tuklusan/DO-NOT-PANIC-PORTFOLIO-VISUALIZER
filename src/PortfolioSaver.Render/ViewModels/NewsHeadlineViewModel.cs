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
using System.Windows.Media;
using PortfolioSaver.Shared.Infrastructure;

namespace PortfolioSaver.Render.ViewModels;

public sealed class NewsHeadlineViewModel : BindableBase
{
    private string _text = string.Empty;
    private Brush _foreground = Brushes.WhiteSmoke;
    private bool _isSupplemental;

    public string Text
    {
        get => _text;
        set => SetProperty(ref _text, value);
    }

    public Brush Foreground
    {
        get => _foreground;
        set => SetProperty(ref _foreground, value);
    }

    public bool IsSupplemental
    {
        get => _isSupplemental;
        set => SetProperty(ref _isSupplemental, value);
    }
}
