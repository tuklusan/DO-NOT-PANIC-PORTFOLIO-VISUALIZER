using PortfolioSaver.Render.Services;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class FloatingClockBuilderTests
{
    [Fact]
    public void BuildDefault_CreatesLocalSummaryPlusElevenExchangeCards()
    {
        FloatingClockBuilder builder = new();
        var clock = builder.BuildDefault();

        Assert.Equal(12, clock.Cities.Count);
        Assert.Equal("Global Markets", clock.Title);
        Assert.Equal(string.Empty, clock.Subtitle);
        Assert.Equal(940, clock.Width);
        Assert.Equal(184, clock.Height);

        var local = clock.Cities[0];
        Assert.True(local.IsLocalSummary);
        Assert.False(local.ShowExchangeDetails);
        Assert.True(local.SupportsWeather);

        var exchanges = clock.Cities.Skip(1).ToList();
        Assert.Equal(11, exchanges.Count);
        Assert.All(exchanges, city =>
        {
            Assert.False(city.IsLocalSummary);
            Assert.True(city.ShowExchangeDetails);
            Assert.False(string.IsNullOrWhiteSpace(city.FlagCode));
            Assert.False(string.IsNullOrWhiteSpace(city.ExchangeName));
            Assert.False(string.IsNullOrWhiteSpace(city.ExchangeSymbol));
            Assert.False(string.IsNullOrWhiteSpace(city.CalendarExchangeCode));
        });
    }

    [Fact]
    public void GetWorldIndexSymbols_MatchesExchangeCards()
    {
        FloatingClockBuilder builder = new();
        var clock = builder.BuildDefault();
        IReadOnlyList<string> symbols = FloatingClockBuilder.GetWorldIndexSymbols();
        var cardSymbols = clock.Cities
            .Where(city => city.ShowExchangeDetails)
            .Select(city => city.ExchangeSymbol)
            .ToList();

        Assert.Equal(11, symbols.Count);
        Assert.Equal(symbols, cardSymbols);
        Assert.Equal(symbols.Count, symbols.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains("^SPX", symbols);
        Assert.Contains("INDY.US", symbols);
        Assert.Contains("EWA.US", symbols);
        Assert.DoesNotContain("^NYA", symbols);
        Assert.DoesNotContain("^NSEI", symbols);
        Assert.DoesNotContain("^AXJO", symbols);
    }

    [Fact]
    public void BuildDefault_UsesTransparentSubstitutesForUnsupportedExactIndexFeeds()
    {
        FloatingClockBuilder builder = new();
        var clock = builder.BuildDefault();

        var newYork = clock.Cities.Single(city => city.Key == "NewYork");
        var mumbai = clock.Cities.Single(city => city.Key == "Mumbai");
        var sydney = clock.Cities.Single(city => city.Key == "Sydney");

        Assert.Equal("S&P 500", newYork.ExchangeName);
        Assert.Equal("^SPX", newYork.ExchangeSymbol);
        Assert.Equal("US", newYork.FlagCode);
        Assert.Equal("India 50 ETF", mumbai.ExchangeName);
        Assert.Equal("INDY.US", mumbai.ExchangeSymbol);
        Assert.Equal("IN", mumbai.FlagCode);
        Assert.Equal("MSCI Australia ETF", sydney.ExchangeName);
        Assert.Equal("EWA.US", sydney.ExchangeSymbol);
        Assert.Equal("AU", sydney.FlagCode);
    }
}
