using System.Text.RegularExpressions;
using PortfolioSaver.Core.Enums;
using PortfolioSaver.Core.Models;

namespace PortfolioSaver.Core.Services;

public static partial class DataSourceSymbolEligibility
{
    private static readonly HashSet<string> SharedUnsupportedIndexSymbols = new(StringComparer.OrdinalIgnoreCase)
    {
        "DXY",
        "TNX",
        "US2M",
        "US10Y",
        "VIX",
        "DX-Y.NYB",
        "SWVXX"
    };

    public static bool IsEligible(DataSourceKind kind, string? symbol)
    {
        string normalized = Normalize(symbol);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        SymbolAssetClass assetClass = SymbolProfileHeuristics.InferAssetClass(normalized);
        if (assetClass != SymbolAssetClass.Unknown)
            return IsEligibleByAssetClass(kind, normalized, assetClass);

        return IsEligibleBySyntax(kind, normalized);
    }

    public static bool IsEligible(DataSourceKind kind, string? symbol, SymbolProfile? profile)
    {
        string normalized = Normalize(symbol);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        if (profile is not null)
        {
            if (profile.SupportedQuoteSources.Count > 0)
                return profile.SupportedQuoteSources.Contains(kind);

            if (profile.AssetClass != SymbolAssetClass.Unknown)
                return IsEligibleByAssetClass(kind, normalized, profile.AssetClass);
        }

        return IsEligible(kind, normalized);
    }

    public static bool IsEligible(
        DataSourceKind kind,
        string? symbol,
        IReadOnlyDictionary<string, SymbolProfile>? profiles)
    {
        string normalized = Normalize(symbol);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        if (profiles is not null &&
            profiles.TryGetValue(normalized, out SymbolProfile? profile))
        {
            return IsEligible(kind, normalized, profile);
        }

        return IsEligible(kind, normalized);
    }

    public static bool IsHistoryEligible(DataSourceKind kind, string? symbol)
    {
        string normalized = Normalize(symbol);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        SymbolAssetClass assetClass = SymbolProfileHeuristics.InferAssetClass(normalized);
        return IsHistoryEligibleByAssetClass(kind, normalized, assetClass);
    }

    public static bool IsHistoryEligible(DataSourceKind kind, string? symbol, SymbolProfile? profile)
    {
        string normalized = Normalize(symbol);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        if (profile is not null)
        {
            if (profile.SupportedHistorySources.Count > 0)
                return profile.SupportedHistorySources.Contains(kind);

            if (profile.AssetClass != SymbolAssetClass.Unknown)
                return IsHistoryEligibleByAssetClass(kind, normalized, profile.AssetClass);
        }

        return IsHistoryEligible(kind, normalized);
    }

    public static bool IsHistoryEligible(
        DataSourceKind kind,
        string? symbol,
        IReadOnlyDictionary<string, SymbolProfile>? profiles)
    {
        string normalized = Normalize(symbol);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        if (profiles is not null &&
            profiles.TryGetValue(normalized, out SymbolProfile? profile))
        {
            return IsHistoryEligible(kind, normalized, profile);
        }

        return IsHistoryEligible(kind, normalized);
    }

    private static bool IsEligibleByAssetClass(DataSourceKind kind, string normalizedSymbol, SymbolAssetClass assetClass)
    {
        return assetClass switch
        {
            SymbolAssetClass.Index => kind is DataSourceKind.Finnhub or DataSourceKind.YahooFinance ||
                                      (kind == DataSourceKind.TwelveData &&
                                       ProviderSymbolAliasCatalog.TryGetQuoteAlias(kind, normalizedSymbol, out _)),
            SymbolAssetClass.Future => kind is DataSourceKind.Finnhub or DataSourceKind.YahooFinance,
            SymbolAssetClass.Forex => kind is DataSourceKind.YahooFinance ||
                                      (kind == DataSourceKind.TwelveData &&
                                       ProviderSymbolAliasCatalog.TryGetQuoteAlias(kind, normalizedSymbol, out _)),
            SymbolAssetClass.Crypto => kind is DataSourceKind.YahooFinance,
            SymbolAssetClass.MutualFund or SymbolAssetClass.MoneyMarketFund => kind is DataSourceKind.YahooFinance,
            SymbolAssetClass.PreferredShare or SymbolAssetClass.Adr or SymbolAssetClass.Equity or SymbolAssetClass.ExchangeTradedFund => IsEligibleBySyntax(kind, normalizedSymbol),
            _ => IsEligibleBySyntax(kind, normalizedSymbol)
        };
    }

    private static bool IsHistoryEligibleByAssetClass(DataSourceKind kind, string normalizedSymbol, SymbolAssetClass assetClass)
    {
        return kind switch
        {
            DataSourceKind.Tiingo => false,
            DataSourceKind.Finnhub => assetClass is
                SymbolAssetClass.Unknown or
                SymbolAssetClass.Equity or
                SymbolAssetClass.ExchangeTradedFund or
                SymbolAssetClass.Adr or
                SymbolAssetClass.PreferredShare or
                SymbolAssetClass.Index or
                SymbolAssetClass.Future,
            DataSourceKind.TwelveData => IsEligibleBySyntax(kind, normalizedSymbol) &&
                                         (assetClass == SymbolAssetClass.Unknown ||
                                          assetClass == SymbolAssetClass.Equity ||
                                          assetClass == SymbolAssetClass.ExchangeTradedFund ||
                                          assetClass == SymbolAssetClass.Adr ||
                                          assetClass == SymbolAssetClass.PreferredShare),
            DataSourceKind.YahooFinance => true,
            _ => IsEligibleBySyntax(kind, normalizedSymbol)
        };
    }

    private static bool IsEligibleBySyntax(DataSourceKind kind, string normalized)
    {
        return kind switch
        {
            DataSourceKind.TwelveData =>
                ProviderSymbolAliasCatalog.TryGetQuoteAlias(kind, normalized, out _) ||
                (IsEquityStyleTicker(normalized) && !SharedUnsupportedIndexSymbols.Contains(normalized)),
            DataSourceKind.Tiingo => IsEquityStyleTicker(normalized) && !SharedUnsupportedIndexSymbols.Contains(normalized),
            _ => true
        };
    }

    private static bool IsEquityStyleTicker(string symbol)
        => EquityStyleTickerRegex().IsMatch(symbol) &&
           !symbol.Contains('=') &&
           !symbol.Contains('^') &&
           !symbol.Contains('/');

    private static string Normalize(string? symbol)
        => (symbol ?? string.Empty).Trim().ToUpperInvariant();

    [GeneratedRegex("^[A-Z][A-Z0-9.-]{0,9}$", RegexOptions.CultureInvariant)]
    private static partial Regex EquityStyleTickerRegex();
}
