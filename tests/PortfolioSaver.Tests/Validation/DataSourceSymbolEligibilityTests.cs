using PortfolioSaver.Core.Enums;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Core.Services;
using Xunit;

namespace PortfolioSaver.Tests.Validation;

public sealed class DataSourceSymbolEligibilityTests
{
    [Theory]
    [InlineData(DataSourceKind.TwelveData, "ES=F", false)]
    [InlineData(DataSourceKind.Tiingo, "ES=F", false)]
    [InlineData(DataSourceKind.YahooFinance, "ES=F", true)]
    [InlineData(DataSourceKind.TwelveData, "VIX", false)]
    [InlineData(DataSourceKind.TwelveData, "^FTSE", false)]
    [InlineData(DataSourceKind.TwelveData, "DX-Y.NYB", false)]
    [InlineData(DataSourceKind.TwelveData, "US2M", false)]
    [InlineData(DataSourceKind.YahooFinance, "US2M", true)]
    [InlineData(DataSourceKind.Tiingo, "SWVXX", false)]
    [InlineData(DataSourceKind.YahooFinance, "SWVXX", true)]
    [InlineData(DataSourceKind.Tiingo, "BTC-USD", false)]
    [InlineData(DataSourceKind.YahooFinance, "BTC-USD", true)]
    public void IsEligible_UsesHeuristicsForMixedSymbols(DataSourceKind kind, string symbol, bool expected)
    {
        Assert.Equal(expected, DataSourceSymbolEligibility.IsEligible(kind, symbol));
    }

    [Fact]
    public void IsEligible_UsesProfileSupportedSourcesWhenAvailable()
    {
        SymbolProfile profile = new()
        {
            Symbol = "SWVXX",
            AssetClass = SymbolAssetClass.MoneyMarketFund,
            SupportedQuoteSources = [DataSourceKind.TwelveData, DataSourceKind.YahooFinance]
        };

        Assert.True(DataSourceSymbolEligibility.IsEligible(DataSourceKind.TwelveData, "SWVXX", profile));
        Assert.False(DataSourceSymbolEligibility.IsEligible(DataSourceKind.Tiingo, "SWVXX", profile));
    }

    [Theory]
    [InlineData(DataSourceKind.Finnhub, "ES=F", true)]
    [InlineData(DataSourceKind.TwelveData, "ES=F", false)]
    [InlineData(DataSourceKind.YahooFinance, "ES=F", true)]
    [InlineData(DataSourceKind.Finnhub, "SWVXX", false)]
    [InlineData(DataSourceKind.YahooFinance, "SWVXX", true)]
    [InlineData(DataSourceKind.Tiingo, "AAPL", false)]
    public void IsHistoryEligible_UsesAssetClassAwareRules(DataSourceKind kind, string symbol, bool expected)
    {
        Assert.Equal(expected, DataSourceSymbolEligibility.IsHistoryEligible(kind, symbol));
    }

    [Fact]
    public void IsHistoryEligible_UsesProfileSupportedHistorySourcesWhenAvailable()
    {
        SymbolProfile profile = new()
        {
            Symbol = "VTSAX",
            AssetClass = SymbolAssetClass.MutualFund,
            SupportedHistorySources = [DataSourceKind.YahooFinance]
        };

        Assert.True(DataSourceSymbolEligibility.IsHistoryEligible(DataSourceKind.YahooFinance, "VTSAX", profile));
        Assert.False(DataSourceSymbolEligibility.IsHistoryEligible(DataSourceKind.Finnhub, "VTSAX", profile));
    }
}

