using PortfolioSaver.Render.Services;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class FloatingClockBuilderTests
{
    [Fact]
    public void BuildDefault_CreatesLocalSummaryPlusNineteenExchangeCards()
    {
        FloatingClockBuilder builder = new();
        var clock = builder.BuildDefault();

        Assert.Equal(20, clock.Cities.Count);
        Assert.Equal("Global Markets", clock.Title);
        Assert.Equal(string.Empty, clock.Subtitle);
        Assert.Equal(940, clock.Width);
        Assert.Equal(184, clock.Height);

        var local = clock.Cities[0];
        Assert.True(local.IsLocalSummary);
        Assert.False(local.ShowExchangeDetails);
        Assert.True(local.SupportsWeather);

        var exchanges = clock.Cities.Skip(1).ToList();
        Assert.Equal(19, exchanges.Count);
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

        Assert.Equal(19, symbols.Count);
        Assert.Equal(symbols, cardSymbols);
        Assert.Equal(symbols.Count, symbols.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains("^IXIC", symbols);
        Assert.Contains("^NYA", symbols);
        Assert.Contains("^NSEI", symbols);
        Assert.Contains("^AXJO", symbols);
        Assert.Contains("000001.SS", symbols);
        Assert.Contains("399001.SZ", symbols);
    }

    [Fact]
    public void BuildDefault_UsesCanonicalYahooGlobalExchangeBenchmarks()
    {
        FloatingClockBuilder builder = new();
        var clock = builder.BuildDefault();

        var nasdaq = clock.Cities.Single(city => city.Key == "NewYorkNasdaq");
        var nyse = clock.Cities.Single(city => city.Key == "NewYorkNyse");
        var mumbai = clock.Cities.Single(city => city.Key == "Mumbai");
        var sydney = clock.Cities.Single(city => city.Key == "Sydney");

        Assert.Equal("Nasdaq Composite", nasdaq.ExchangeName);
        Assert.Equal("^IXIC", nasdaq.ExchangeSymbol);
        Assert.Equal("US", nasdaq.FlagCode);
        Assert.Equal("NYSE Composite", nyse.ExchangeName);
        Assert.Equal("^NYA", nyse.ExchangeSymbol);
        Assert.Equal("Nifty 50", mumbai.ExchangeName);
        Assert.Equal("^NSEI", mumbai.ExchangeSymbol);
        Assert.Equal("IN", mumbai.FlagCode);
        Assert.Equal("S&P/ASX 200", sydney.ExchangeName);
        Assert.Equal("^AXJO", sydney.ExchangeSymbol);
        Assert.Equal("AU", sydney.FlagCode);
    }
}
