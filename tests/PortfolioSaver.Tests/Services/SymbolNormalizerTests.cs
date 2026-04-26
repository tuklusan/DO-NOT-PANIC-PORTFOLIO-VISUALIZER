using PortfolioSaver.Core.Services;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class SymbolNormalizerTests
{
    [Fact]
    public void Normalize_TrimsAndUppercasesWithoutChangingDotSymbols()
    {
        SymbolNormalizer normalizer = new();
        Assert.Equal("BRK.B", normalizer.Normalize(" brk.b "));
    }

    [Theory]
    [InlineData(" eur/usd ", "EUR/USD")]
    [InlineData("^gspc", "^GSPC")]
    [InlineData(" btc-usd ", "BTC-USD")]
    public void Normalize_PreservesCommonNonEquityTickerCharacters(string input, string expected)
    {
        SymbolNormalizer normalizer = new();
        Assert.Equal(expected, normalizer.Normalize(input));
    }
}
