using PortfolioSaver.Core.Enums;

namespace PortfolioSaver.Core.Models;

public sealed class SymbolProfile
{
    public string Symbol { get; set; } = string.Empty;
    public string CanonicalSymbol { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Exchange { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public SymbolAssetClass AssetClass { get; set; } = SymbolAssetClass.Unknown;
    public string RawInstrumentType { get; set; } = string.Empty;
    public DateTimeOffset LastValidatedUtc { get; set; }
    public string ValidationSummary { get; set; } = string.Empty;
}
