using PortfolioSaver.Core.Enums;
using PortfolioSaver.Core.Models;

namespace PortfolioSaver.Core.Services;

public static class DataSourceSymbolEligibility
{

    public static bool IsEligible(DataSourceKind kind, string? symbol)
    {
        return kind == DataSourceKind.YahooFinance &&
               !string.IsNullOrWhiteSpace(Normalize(symbol));
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
        return kind == DataSourceKind.YahooFinance &&
               !string.IsNullOrWhiteSpace(Normalize(symbol));
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

    private static string Normalize(string? symbol)
        => (symbol ?? string.Empty).Trim().ToUpperInvariant();
}
