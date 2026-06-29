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
