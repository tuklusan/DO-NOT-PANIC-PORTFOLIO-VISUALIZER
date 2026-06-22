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
using System.Net;
using PortfolioSaver.Config.Services;
using YFinance.NET.Protocol.Dtos;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class YahooSymbolValidationServiceTests
{
    [Fact]
    public async Task ValidateAsync_WhenYahooRateLimits_MarksSymbolsDeferredInsteadOfInvalid()
    {
        YahooSymbolValidationService service = new((symbols, _, _) =>
            throw new HttpRequestException("Too Many Requests", null, HttpStatusCode.TooManyRequests));

        YahooSymbolValidationResult result = await service.ValidateAsync(["AAPL", "MSFT"], 5);

        Assert.True(result.WasRateLimited);
        Assert.Empty(result.InvalidSymbols);
        Assert.Equal(["AAPL", "MSFT"], result.DeferredSymbols);
    }

    [Fact]
    public async Task ValidateAsync_WhenServerReturnsCanonicalIndexWithoutCaret_MarksSymbolValid()
    {
        YahooSymbolValidationService service = new((symbols, _, _) =>
        {
            IReadOnlyDictionary<string, QuoteDto> quotes = new Dictionary<string, QuoteDto>(StringComparer.OrdinalIgnoreCase)
            {
                ["GSPTSE"] = new(
                    "GSPTSE",
                    "TSX",
                    "S&P/TSX Composite",
                    "TSX",
                    "CAD",
                    "TOR",
                    "America/Toronto",
                    "EDT",
                    "INDEX",
                    "REGULAR",
                    25000m,
                    24900m,
                    24950m,
                    25100m,
                    24850m,
                    100m,
                    0.4m,
                    null,
                    null,
                    DateTimeOffset.UtcNow,
                    new CacheMetadataDto("server", 0, false))
            };

            return Task.FromResult(quotes);
        });

        YahooSymbolValidationResult result = await service.ValidateAsync(["^GSPTSE"], 5);

        Assert.True(result.Entries["^GSPTSE"].IsValid);
        Assert.Empty(result.InvalidSymbols);
    }
}
