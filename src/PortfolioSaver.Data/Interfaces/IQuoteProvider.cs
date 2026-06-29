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
using PortfolioSaver.Core.Models;

namespace PortfolioSaver.Data.Interfaces;

public interface IQuoteProvider
{
    /// <summary>
    /// Returns all successfully resolved quotes. Missing symbols are omitted; providers should throw only when no usable quote result is available or the request itself fails.
    /// </summary>
    Task<IReadOnlyList<QuoteSnapshot>> GetQuotesAsync(IEnumerable<string> symbols, CancellationToken cancellationToken = default);
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);
}
