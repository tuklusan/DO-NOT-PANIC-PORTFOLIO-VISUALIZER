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
using PortfolioSaver.Core.Enums;
using PortfolioSaver.Core.Services;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class MarketSessionResolverTests
{
    [Fact]
    public void Resolve_ReturnsAValidEnum()
    {
        MarketSessionResolver resolver = new();
        MarketSession session = resolver.Resolve(DateTimeOffset.UtcNow);
        Assert.True(Enum.IsDefined(session));
    }
}
