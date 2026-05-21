using PortfolioSaver.Core.Enums;

namespace PortfolioSaver.Core.Services;

public static class DataSourceSymbolEligibility
{

    public static bool IsEligible(DataSourceKind kind, string? symbol)
    {
        return kind == DataSourceKind.YahooFinance &&
               !string.IsNullOrWhiteSpace(Normalize(symbol));
    }

    public static bool IsHistoryEligible(DataSourceKind kind, string? symbol)
    {
        return kind == DataSourceKind.YahooFinance &&
               !string.IsNullOrWhiteSpace(Normalize(symbol));
    }

    private static string Normalize(string? symbol)
        => (symbol ?? string.Empty).Trim().ToUpperInvariant();
}
