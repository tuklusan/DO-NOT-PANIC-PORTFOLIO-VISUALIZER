using PortfolioSaver.Core.Enums;
using PortfolioSaver.Core.Services;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class SymbolProfileHeuristicsTests
{
    [Theory]
    [InlineData("ES=F", SymbolAssetClass.Future)]
    [InlineData("^GSPC", SymbolAssetClass.Index)]
    [InlineData("^VIX", SymbolAssetClass.Index)]
    [InlineData("^TNX", SymbolAssetClass.Index)]
    [InlineData("^IRX", SymbolAssetClass.Index)]
    [InlineData("EUR/USD", SymbolAssetClass.Forex)]
    [InlineData("EURUSD=X", SymbolAssetClass.Forex)]
    [InlineData("BTC-USD", SymbolAssetClass.Crypto)]
    [InlineData("SWVXX", SymbolAssetClass.MoneyMarketFund)]
    [InlineData("VTSAX", SymbolAssetClass.MutualFund)]
    public void InferAssetClass_RecognizesMixedTickerShapes(string symbol, SymbolAssetClass expected)
    {
        Assert.Equal(expected, SymbolProfileHeuristics.InferAssetClass(symbol));
    }

    [Theory]
    [InlineData("AAPL", "EQUITY", SymbolAssetClass.Equity)]
    [InlineData("SPY", "ETF", SymbolAssetClass.ExchangeTradedFund)]
    [InlineData("VTSAX", "MUTUALFUND", SymbolAssetClass.MutualFund)]
    [InlineData("BTC-USD", "CRYPTOCURRENCY", SymbolAssetClass.Crypto)]
    public void InferAssetClass_PrefersProviderInstrumentType(string symbol, string instrumentType, SymbolAssetClass expected)
    {
        Assert.Equal(expected, SymbolProfileHeuristics.InferAssetClass(symbol, instrumentType));
    }
}

