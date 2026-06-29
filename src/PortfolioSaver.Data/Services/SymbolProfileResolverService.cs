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
using PortfolioSaver.Core.Models;
using PortfolioSaver.Core.Services;
using PortfolioSaver.Shared.Diagnostics;
using YFinance.NET.Protocol.Dtos;

namespace PortfolioSaver.Data.Services;

public sealed class SymbolProfileResolverService
{
    private readonly SymbolNormalizer _symbolNormalizer = new();

    public SymbolProfileResolverService(HttpClient httpClient)
    {
    }

    public async Task<ResolvedSymbolProfile> ResolveAsync(
        string symbol,
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        string normalizedSymbol = _symbolNormalizer.Normalize(symbol);
        SymbolProfile profile = new()
        {
            Symbol = normalizedSymbol,
            CanonicalSymbol = normalizedSymbol,
            AssetClass = SymbolProfileHeuristics.InferAssetClass(normalizedSymbol),
            LastValidatedUtc = DateTimeOffset.UtcNow
        };

        try
        {
            string requestSymbol = YFinanceSymbolMapper.ToRequestSymbol(normalizedSymbol);
            string operationId = YFinanceRuntimeClientFactory.CreateOperationId("symbol-profile");
            TraceLog.InfoState(
                "YFinanceUiBridge",
                "SymbolProfileRequestStart",
                [new("operation_id", operationId), new("symbol", normalizedSymbol), new("request_symbol", requestSymbol)]);
            TickerInfoDto info = await YFinanceRuntimeClientFactory
                .RunSerializedAsync(
                    "symbol-profile",
                    operationId,
                    (client, token) => client.GetTickerInfoAsync(requestSymbol, token),
                    cancellationToken)
                .ConfigureAwait(false);
            if (info.RegularMarketPrice is null && info.RegularMarketPreviousClose is null)
            {
                TraceLog.WarnState(
                    "YFinanceUiBridge",
                    "SymbolProfileRequestEmpty",
                    [new("operation_id", operationId), new("symbol", normalizedSymbol), new("request_symbol", requestSymbol)]);
                profile.ValidationSummary = $"'{normalizedSymbol}' could not be validated against YFinance.NET.";
                return ResolvedSymbolProfile.Invalid(profile, profile.ValidationSummary);
            }

            ApplyMetadata(profile, normalizedSymbol, info);
            TraceLog.InfoState(
                "YFinanceUiBridge",
                "SymbolProfileRequestComplete",
                [new("operation_id", operationId), new("symbol", normalizedSymbol), new("display_name", profile.DisplayName), new("exchange", profile.Exchange)]);
            profile.ValidationSummary = "Validated via YFinance.NET.";
            return ResolvedSymbolProfile.Valid(profile);
        }
        catch (Exception ex)
        {
            TraceLog.WarnState(
                "YFinanceUiBridge",
                "SymbolProfileRequestFailed",
                [new("symbol", normalizedSymbol), new("message", ex.Message)]);
            profile.ValidationSummary = $"Validation through YFinance.NET was inconclusive: {ex.Message}";
            return ResolvedSymbolProfile.Indeterminate(profile, profile.ValidationSummary);
        }
    }

    private static void ApplyMetadata(SymbolProfile profile, string normalizedSymbol, TickerInfoDto info)
    {
        profile.CanonicalSymbol = normalizedSymbol;
        profile.DisplayName = info.DisplayName?.Trim()
                              ?? info.ShortName?.Trim()
                              ?? info.LongName?.Trim()
                              ?? profile.DisplayName;
        profile.Exchange = info.Exchange?.Trim() ?? string.Empty;
        profile.Currency = info.Currency?.Trim() ?? string.Empty;
        profile.RawInstrumentType = info.QuoteType?.Trim() ?? string.Empty;

        SymbolAssetClass inferred = SymbolProfileHeuristics.InferAssetClass(profile.Symbol, profile.RawInstrumentType);
        if (inferred != SymbolAssetClass.Unknown)
            profile.AssetClass = inferred;
    }
}

public readonly record struct ResolvedSymbolProfile(
    SymbolProfile Profile,
    SymbolProfileResolutionStatus Status,
    string Message)
{
    public static ResolvedSymbolProfile Valid(SymbolProfile profile)
        => new(profile, SymbolProfileResolutionStatus.Valid, string.Empty);

    public static ResolvedSymbolProfile Invalid(SymbolProfile profile, string message)
        => new(profile, SymbolProfileResolutionStatus.Invalid, message);

    public static ResolvedSymbolProfile Indeterminate(SymbolProfile profile, string message)
        => new(profile, SymbolProfileResolutionStatus.Indeterminate, message);
}

public enum SymbolProfileResolutionStatus
{
    Valid,
    Invalid,
    Indeterminate
}
