using System.Reflection;
using System.Windows.Media;
using PortfolioSaver.Core.Constants;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Render.ViewModels;
using PortfolioSaver.Screensaver.Services;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class StartupCoordinatorTapeItemTests
{
    [Fact]
    public void BuildTapeItem_NullQuote_UsesGoldSymbolAndBlankValues()
    {
        DateTimeOffset now = new(2026, 4, 10, 12, 0, 0, TimeSpan.Zero);

        TapeItemViewModel item = InvokeBuildTapeItem("AAPL", null, "Apple", now);

        Assert.Equal(Brushes.Gold, item.SymbolForeground);
        Assert.True(item.IsWaitingOnData);
        Assert.Equal(string.Empty, item.LastText);
        Assert.Equal(string.Empty, item.ChangeText);
    }

    [Fact]
    public void BuildTapeItem_StaleQuote_UsesGoldSymbolAndBlankValues()
    {
        DateTimeOffset now = new(2026, 4, 10, 12, 0, 0, TimeSpan.Zero);
        QuoteSnapshot staleQuote = new()
        {
            Symbol = "MSFT",
            Last = 100m,
            ChangePercent = 1m,
            FetchTimestampUtc = now.AddMinutes(-23),
            IsStale = false
        };

        TapeItemViewModel item = InvokeBuildTapeItem("MSFT", staleQuote, "Microsoft", now);

        Assert.Equal(Brushes.Gold, item.SymbolForeground);
        Assert.True(item.IsWaitingOnData);
        Assert.Equal(string.Empty, item.LastText);
        Assert.Equal(string.Empty, item.ChangeText);
    }

    [Fact]
    public void BuildTapeItem_FreshQuote_UsesChangeBrushAndValueTexts()
    {
        DateTimeOffset now = new(2026, 4, 10, 12, 0, 0, TimeSpan.Zero);
        QuoteSnapshot quote = new()
        {
            Symbol = "NVDA",
            Last = 250.12m,
            ChangePercent = 2.50m,
            FetchTimestampUtc = now.AddSeconds(-2),
            IsStale = false
        };

        TapeItemViewModel item = InvokeBuildTapeItem("NVDA", quote, "NVIDIA", now);

        Assert.Equal(Brushes.LimeGreen, item.SymbolForeground);
        Assert.False(item.IsWaitingOnData);
        Assert.Equal("250.12", item.LastText);
        Assert.Equal("+2.50%", item.ChangeText);
    }

    [Fact]
    public void BuildTapeItem_UsesFetchTimestampNotProviderTimestampForStaleDecision()
    {
        DateTimeOffset now = new(2026, 4, 10, 12, 0, 0, TimeSpan.Zero);
        QuoteSnapshot quote = new()
        {
            Symbol = "SPY",
            Last = 500.00m,
            ChangePercent = -0.10m,
            ProviderTimestampUtc = now.AddDays(-3),
            FetchTimestampUtc = now.AddSeconds(-3),
            IsStale = false
        };

        TapeItemViewModel item = InvokeBuildTapeItem("SPY", quote, "SPY", now);

        Assert.NotEqual(Brushes.Gold, item.SymbolForeground);
        Assert.Equal("500.00", item.LastText);
        Assert.Equal("-0.10%", item.ChangeText);
    }

    private static TapeItemViewModel InvokeBuildTapeItem(string symbol, QuoteSnapshot? quote, string displayName, DateTimeOffset nowUtc)
    {
        MethodInfo? method = typeof(StartupCoordinator).GetMethod(
            "BuildTapeItem",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        AppSettings settings = Defaults.CreateSettings();
        object? value = method!.Invoke(null, [symbol, quote, displayName, settings, nowUtc]);
        return Assert.IsType<TapeItemViewModel>(value);
    }
}
