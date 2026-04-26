using PortfolioSaver.Core.Enums;

namespace PortfolioSaver.Core.Services;

public static class ProviderSymbolAliasCatalog
{
    private static readonly Dictionary<string, string> TwelveDataAliases = new(StringComparer.OrdinalIgnoreCase)
    {
    };

    public static bool TryGetQuoteAlias(DataSourceKind kind, string? symbol, out string providerSymbol)
    {
        string normalized = SymbolProfileHeuristics.Normalize(symbol);
        providerSymbol = string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        if (kind != DataSourceKind.TwelveData)
            return false;

        if (TwelveDataAliases.TryGetValue(normalized, out string? mapped))
        {
            providerSymbol = mapped;
            return true;
        }

        return false;
    }

    public static string GetQuoteRequestSymbol(DataSourceKind kind, string? symbol)
    {
        string normalized = SymbolProfileHeuristics.Normalize(symbol);
        if (TryGetQuoteAlias(kind, normalized, out string providerSymbol))
            return providerSymbol;

        return normalized;
    }
}
