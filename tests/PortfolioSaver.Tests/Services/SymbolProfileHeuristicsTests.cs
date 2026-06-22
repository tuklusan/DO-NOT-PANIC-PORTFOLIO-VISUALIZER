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
using PortfolioSaver.Core.Services;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class SymbolProfileHeuristicsTests
{
    [Theory]
    [InlineData("ES=F", SymbolAssetClass.Future)]
    [InlineData("^GSPC", SymbolAssetClass.Index)]
    [InlineData("^VIX", SymbolAssetClass.Index)]
    [InlineData("^TNX", SymbolAssetClass.Index)]
    [InlineData("^IRX", SymbolAssetClass.Index)]
    [InlineData("EUR/USD", SymbolAssetClass.Forex)]
    [InlineData("EURUSD=X", SymbolAssetClass.Forex)]
    [InlineData("BTC-USD", SymbolAssetClass.Crypto)]
    [InlineData("SWVXX", SymbolAssetClass.MoneyMarketFund)]
    [InlineData("VTSAX", SymbolAssetClass.MutualFund)]
    public void InferAssetClass_RecognizesMixedTickerShapes(string symbol, SymbolAssetClass expected)
    {
        Assert.Equal(expected, SymbolProfileHeuristics.InferAssetClass(symbol));
    }

    [Theory]
    [InlineData("AAPL", "EQUITY", SymbolAssetClass.Equity)]
    [InlineData("SPY", "ETF", SymbolAssetClass.ExchangeTradedFund)]
    [InlineData("VTSAX", "MUTUALFUND", SymbolAssetClass.MutualFund)]
    [InlineData("BTC-USD", "CRYPTOCURRENCY", SymbolAssetClass.Crypto)]
    public void InferAssetClass_PrefersProviderInstrumentType(string symbol, string instrumentType, SymbolAssetClass expected)
    {
        Assert.Equal(expected, SymbolProfileHeuristics.InferAssetClass(symbol, instrumentType));
    }
}
