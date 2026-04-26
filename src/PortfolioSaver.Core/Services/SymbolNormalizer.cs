namespace PortfolioSaver.Core.Services;

public sealed class SymbolNormalizer
{
    public string Normalize(string symbol)
        => SymbolProfileHeuristics.Normalize(symbol).Replace(" ", string.Empty);
}
