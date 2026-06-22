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
using PortfolioSaver.Core.Enums;
using System.Collections.ObjectModel;
using PortfolioSaver.Shared.Infrastructure;

namespace PortfolioSaver.Render.ViewModels;

public sealed class TapeViewModel : BindableBase
{
    private string _title = string.Empty;
    private double _speed = 1.0d;
    private ScrollDirection _direction = ScrollDirection.Left;

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

    public ScrollDirection Direction
    {
        get => _direction;
        set => SetProperty(ref _direction, value);
    }

    public ObservableCollection<TapeItemViewModel> Items { get; set; } = [];
}
