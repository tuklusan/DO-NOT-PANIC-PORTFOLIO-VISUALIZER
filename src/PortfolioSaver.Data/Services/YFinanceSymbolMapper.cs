using PortfolioSaver.Core.Enums;

namespace PortfolioSaver.Data.Services;

public static class YFinanceSymbolMapper
{
    private static readonly IReadOnlyDictionary<string, string> RequestAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["INDY.US"] = "INDY",
        ["EWA.US"] = "EWA",
        ["US10Y"] = "^TNX",
        ["US2M"] = "^IRX"
    };

    public static string Normalize(string? symbol)
        => (symbol ?? string.Empty).Trim().ToUpperInvariant();

    public static string ToRequestSymbol(string? symbol)
    {
        string normalized = Normalize(symbol);
        return RequestAliases.TryGetValue(normalized, out string? mapped) ? mapped : normalized;
    }

    public static decimal? NormalizeNumericValue(string requestedSymbol, decimal? value)
    {
        if (!value.HasValue)
            return null;

        string normalized = Normalize(requestedSymbol);
        if (normalized is "US10Y" or "US2M")
            return Math.Round(value.Value / 10m, 3);

        return value;
    }

    public static MarketSession MapMarketSession(string? marketState)
    {
        if (string.IsNullOrWhiteSpace(marketState))
            return MarketSession.Unknown;

        return marketState.Trim().ToUpperInvariant() switch
        {
            "PRE" or "PREPRE" or "PREPREMARKET" or "PREMARKET" => MarketSession.PreMarket,
            "REGULAR" or "OPEN" => MarketSession.Regular,
            "POST" or "POSTPOST" or "POSTMARKET" or "AFTER_HOURS" => MarketSession.AfterHours,
            "CLOSED" => MarketSession.Closed,
            _ => MarketSession.Unknown
        };
    }
}
