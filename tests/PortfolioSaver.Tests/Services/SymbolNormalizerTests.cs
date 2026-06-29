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
using PortfolioSaver.Core.Services;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class SymbolNormalizerTests
{
    [Fact]
    public void Normalize_TrimsAndUppercasesWithoutChangingDotSymbols()
    {
        SymbolNormalizer normalizer = new();
        Assert.Equal("BRK.B", normalizer.Normalize(" brk.b "));
    }

    [Theory]
    [InlineData(" eur/usd ", "EUR/USD")]
    [InlineData("^gspc", "^GSPC")]
    [InlineData(" btc-usd ", "BTC-USD")]
    public void Normalize_PreservesCommonNonEquityTickerCharacters(string input, string expected)
    {
        SymbolNormalizer normalizer = new();
        Assert.Equal(expected, normalizer.Normalize(input));
    }
}
