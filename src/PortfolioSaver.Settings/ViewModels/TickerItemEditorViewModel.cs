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
using PortfolioSaver.Config.Commands;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Core.Services;
using PortfolioSaver.Shared.Infrastructure;

namespace PortfolioSaver.Config.ViewModels;

public sealed class TickerItemEditorViewModel : BindableBase
{
    private string _symbol;
    private string _displayName;
    private decimal? _quantity;
    private decimal? _costBasis;
    private string _currency;
    private bool _enabled;
    private SymbolValidationState _validationState;
    private string _validationMessage;

    public TickerItemEditorViewModel(TickerItem? item = null, Action<TickerItemEditorViewModel>? removeAction = null)
    {
        item ??= new TickerItem();
        _symbol = item.Symbol;
        _displayName = item.DisplayName;
        _quantity = item.Quantity;
        _costBasis = item.CostBasis;
        _currency = string.IsNullOrWhiteSpace(item.Currency) ? "USD" : item.Currency;
        _enabled = item.Enabled;
        _validationState = SymbolValidationState.Unknown;
        _validationMessage = "Pending validation";
        RemoveCommand = new RelayCommand(() => removeAction?.Invoke(this));
    }

    public string Symbol
    {
        get => _symbol;
        set
        {
            string previousSymbol = _symbol;
            if (!SetProperty(ref _symbol, value))
                return;

            if (!string.Equals(
                    SymbolProfileHeuristics.Normalize(previousSymbol),
                    SymbolProfileHeuristics.Normalize(_symbol),
                    StringComparison.OrdinalIgnoreCase))
            {
                DisplayName = string.Empty;
            }
        }
    }

    public string DisplayName
    {
        get => _displayName;
        set => SetProperty(ref _displayName, value);
    }

    public decimal? Quantity
    {
        get => _quantity;
        set => SetProperty(ref _quantity, value);
    }

    public decimal? CostBasis
    {
        get => _costBasis;
        set => SetProperty(ref _costBasis, value);
    }

    public string Currency
    {
        get => _currency;
        set => SetProperty(ref _currency, value);
    }

    public bool Enabled
    {
        get => _enabled;
        set => SetProperty(ref _enabled, value);
    }

    public SymbolValidationState ValidationState
    {
        get => _validationState;
        set
        {
            if (!SetProperty(ref _validationState, value))
                return;

            RaisePropertyChanged(nameof(ValidationBadgeText));
        }
    }

    public string ValidationMessage
    {
        get => _validationMessage;
        set => SetProperty(ref _validationMessage, value);
    }

    public string ValidationBadgeText => ValidationState switch
    {
        SymbolValidationState.Checking => "Checking",
        SymbolValidationState.Valid => "Valid",
        SymbolValidationState.Invalid => "Invalid",
        _ => "Pending"
    };

    public RelayCommand RemoveCommand { get; }

    public TickerItem ToModel()
    {
        return new TickerItem
        {
            Symbol = Symbol.Trim(),
            DisplayName = DisplayName.Trim(),
            Quantity = null,
            CostBasis = null,
            Currency = "USD",
            Enabled = Enabled
        };
    }
}
