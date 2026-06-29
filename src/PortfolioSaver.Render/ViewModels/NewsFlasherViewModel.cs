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
using System.Collections.ObjectModel;
using PortfolioSaver.Core.Constants;
using PortfolioSaver.Shared.Infrastructure;

namespace PortfolioSaver.Render.ViewModels;

public sealed class NewsFlasherViewModel : BindableBase
{
    private string _title = "FINANCE NEWS";
    private double _speed = Defaults.DefaultNewsSpeed;

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public double Speed
    {
        get => _speed;
        set => SetProperty(ref _speed, value);
    }

    public ObservableCollection<NewsHeadlineViewModel> Headlines { get; set; } = [];
}
