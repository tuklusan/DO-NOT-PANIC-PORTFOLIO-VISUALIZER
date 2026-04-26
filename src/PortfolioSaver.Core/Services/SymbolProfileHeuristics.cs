using System.Text.RegularExpressions;
using PortfolioSaver.Core.Enums;

namespace PortfolioSaver.Core.Services;

public static partial class SymbolProfileHeuristics
{
    private static readonly HashSet<string> IndexSymbols = new(StringComparer.OrdinalIgnoreCase)
    {
        "DXY",
        "TNX",
        "US2M",
        "US10Y",
        "VIX",
        "DX-Y.NYB"
    };

    public static SymbolAssetClass InferAssetClass(string? symbol, string? rawInstrumentType = null)
    {
        string normalized = Normalize(symbol);
        string instrumentType = Normalize(rawInstrumentType);

        if (!string.IsNullOrWhiteSpace(instrumentType))
        {
            return instrumentType switch
            {
                "EQUITY" => SymbolAssetClass.Equity,
                "ETF" => SymbolAssetClass.ExchangeTradedFund,
                "MUTUALFUND" => InferFundAssetClass(normalized),
                "INDEX" => SymbolAssetClass.Index,
                "FUTURE" => SymbolAssetClass.Future,
                "CURRENCY" => SymbolAssetClass.Forex,
                "CRYPTOCURRENCY" => SymbolAssetClass.Crypto,
                "ADR" => SymbolAssetClass.Adr,
                "PREFERRED" => SymbolAssetClass.PreferredShare,
                _ => SymbolAssetClass.Unknown
            };
        }

        if (normalized.Contains("=F", StringComparison.OrdinalIgnoreCase))
            return SymbolAssetClass.Future;

        if (normalized.StartsWith("^", StringComparison.OrdinalIgnoreCase) || IndexSymbols.Contains(normalized))
            return SymbolAssetClass.Index;

        if (normalized.EndsWith("=X", StringComparison.OrdinalIgnoreCase) || ForexPairRegex().IsMatch(normalized))
            return SymbolAssetClass.Forex;

        if (CryptoPairRegex().IsMatch(normalized))
            return SymbolAssetClass.Crypto;

        if (MutualFundRegex().IsMatch(normalized))
            return InferFundAssetClass(normalized);

        if (normalized.Contains(".P", StringComparison.OrdinalIgnoreCase) || normalized.Contains("-P", StringComparison.OrdinalIgnoreCase))
            return SymbolAssetClass.PreferredShare;

        return SymbolAssetClass.Unknown;
    }

    public static string Normalize(string? symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return string.Empty;

        return symbol.Trim().ToUpperInvariant();
    }

    private static SymbolAssetClass InferFundAssetClass(string normalizedSymbol)
        => normalizedSymbol.EndsWith("XX", StringComparison.OrdinalIgnoreCase) ||
           normalizedSymbol.Contains("CASH", StringComparison.OrdinalIgnoreCase)
            ? SymbolAssetClass.MoneyMarketFund
            : SymbolAssetClass.MutualFund;

    [GeneratedRegex("^[A-Z]{3}/[A-Z]{3}$|^[A-Z]{6}=X$", RegexOptions.CultureInvariant)]
    private static partial Regex ForexPairRegex();

    [GeneratedRegex("^[A-Z0-9]{2,10}-[A-Z]{3,5}$", RegexOptions.CultureInvariant)]
    private static partial Regex CryptoPairRegex();

    [GeneratedRegex("^[A-Z]{4,5}X$", RegexOptions.CultureInvariant)]
    private static partial Regex MutualFundRegex();
}

