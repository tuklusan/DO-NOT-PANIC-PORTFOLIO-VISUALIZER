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
using PortfolioSaver.Shared.Infrastructure;
using System.Collections.ObjectModel;
using System.Windows.Media;

namespace PortfolioSaver.Render.ViewModels;

public sealed class StatusBarViewModel : BindableBase
{
    private string _marketStatusText = "Market: --";
    private string _updatedPrefixText = "Last Updated:";
    private string _updatedTickerFieldText = string.Empty;
    private Brush _updatedTickerFieldForeground = Brushes.Gainsboro;
    private string _dataFreshnessText = "LOADING - initializing";
    private Brush _dataFreshnessForeground = Brushes.Gainsboro;
    private string _clockDateText = DateTime.Now.ToString("ddd dd-MMM-yyyy").ToUpperInvariant();
    private string _clockText = DateTime.Now.ToLongTimeString();
    private ObservableCollection<MacroMeterViewModel> _macroMeters = [];

    public string MarketStatusText
    {
        get => _marketStatusText;
        set => SetProperty(ref _marketStatusText, value);
    }

    public string UpdatedPrefixText
    {
        get => _updatedPrefixText;
        set => SetProperty(ref _updatedPrefixText, value);
    }

    public string UpdatedTickerFieldText
    {
        get => _updatedTickerFieldText;
        set => SetProperty(ref _updatedTickerFieldText, value);
    }

    public Brush UpdatedTickerFieldForeground
    {
        get => _updatedTickerFieldForeground;
        set => SetProperty(ref _updatedTickerFieldForeground, value);
    }

    public string DataFreshnessText
    {
        get => _dataFreshnessText;
        set => SetProperty(ref _dataFreshnessText, value);
    }

    public Brush DataFreshnessForeground
    {
        get => _dataFreshnessForeground;
        set => SetProperty(ref _dataFreshnessForeground, value);
    }

    public string ClockText
    {
        get => _clockText;
        set => SetProperty(ref _clockText, value);
    }

    public string ClockDateText
    {
        get => _clockDateText;
        set => SetProperty(ref _clockDateText, value);
    }

    public ObservableCollection<MacroMeterViewModel> MacroMeters
    {
        get => _macroMeters;
        set => SetProperty(ref _macroMeters, value);
    }
}
