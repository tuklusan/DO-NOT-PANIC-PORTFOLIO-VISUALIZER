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
