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
    public void BuildTapeItem_NullQuote_UsesLoadingIndicatorAndBlankValues()
    {
        DateTimeOffset now = new(2026, 4, 10, 12, 0, 0, TimeSpan.Zero);

        TapeItemViewModel item = InvokeBuildTapeItem("AAPL", null, "Apple", now);

        Assert.Equal(Brushes.Goldenrod, item.SymbolForeground);
        Assert.True(item.IsWaitingOnData);
        Assert.False(item.HasMissingData);
        Assert.Equal("🕒", item.WaitingGlyphText);
        Assert.Equal(string.Empty, item.LastText);
        Assert.Equal(string.Empty, item.ChangeText);
    }

    [Fact]
    public void BuildTapeItem_ValueLessQuote_UsesMissingIndicatorAndBlankValues()
    {
        DateTimeOffset now = new(2026, 4, 10, 12, 0, 0, TimeSpan.Zero);
        QuoteSnapshot valuelessQuote = new()
        {
            Symbol = "AAPL",
            FetchTimestampUtc = now.AddSeconds(-1),
            IsStale = false
        };

        TapeItemViewModel item = InvokeBuildTapeItem("AAPL", valuelessQuote, "Apple", now);

        Assert.Equal(Brushes.DarkOrange, item.SymbolForeground);
        Assert.True(item.IsWaitingOnData);
        Assert.True(item.HasMissingData);
        Assert.Equal("◌", item.WaitingGlyphText);
        Assert.Equal(string.Empty, item.LastText);
        Assert.Equal(string.Empty, item.ChangeText);
    }

    [Fact]
    public void BuildTapeItem_OlderQuoteStillShowsValues()
    {
        DateTimeOffset now = new(2026, 4, 10, 12, 0, 0, TimeSpan.Zero);
        QuoteSnapshot olderQuote = new()
        {
            Symbol = "MSFT",
            Last = 100m,
            ChangePercent = 1m,
            FetchTimestampUtc = now.AddMinutes(-23),
            IsStale = false
        };

        TapeItemViewModel item = InvokeBuildTapeItem("MSFT", olderQuote, "Microsoft", now);

        Assert.Equal(Brushes.LimeGreen, item.SymbolForeground);
        Assert.False(item.IsWaitingOnData);
        Assert.False(item.HasMissingData);
        Assert.Equal(string.Empty, item.WaitingGlyphText);
        Assert.Equal("100.00", item.LastText);
        Assert.Equal("+1.00%", item.ChangeText);
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
        Assert.False(item.HasMissingData);
        Assert.Equal("250.12", item.LastText);
        Assert.Equal("+2.50%", item.ChangeText);
        Assert.Equal(string.Empty, item.WaitingGlyphText);
    }

    [Fact]
    public void BuildTapeItem_UsesDisplayedValueInsteadOfTimestampAge()
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

        Assert.Equal(Brushes.OrangeRed, item.SymbolForeground);
        Assert.Equal("500.00", item.LastText);
        Assert.Equal("-0.10%", item.ChangeText);
    }

    [Fact]
    public void BuildTapeItem_PreviousCloseOnlyQuote_ShowsValueWithoutWaitingOrMissingState()
    {
        DateTimeOffset now = new(2026, 4, 10, 12, 0, 0, TimeSpan.Zero);
        QuoteSnapshot quote = new()
        {
            Symbol = "BND",
            PreviousClose = 72.34m,
            FetchTimestampUtc = now.AddSeconds(-3),
            IsStale = false
        };

        TapeItemViewModel item = InvokeBuildTapeItem("BND", quote, "BND", now);

        Assert.Equal(Brushes.Gainsboro, item.SymbolForeground);
        Assert.False(item.IsWaitingOnData);
        Assert.False(item.HasMissingData);
        Assert.Equal("72.34", item.LastText);
        Assert.Equal(string.Empty, item.ChangeText);
        Assert.Equal(string.Empty, item.WaitingGlyphText);
    }

    [Fact]
    public void ResolveDataFreshnessText_ReportsLiveOfflineAndStaleStates()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Dictionary<string, QuoteSnapshot> emptyQuotes = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, QuoteSnapshot> liveQuotes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["VOO"] = new QuoteSnapshot { Symbol = "VOO", Last = 688.11m, FetchTimestampUtc = now.AddMinutes(-2), IsStale = false }
        };
        Dictionary<string, QuoteSnapshot> staleQuotes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["VOO"] = new QuoteSnapshot { Symbol = "VOO", Last = 688.11m, FetchTimestampUtc = now.AddMinutes(-2), IsStale = true }
        };
        Dictionary<string, QuoteSnapshot> agedQuotes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["VOO"] = new QuoteSnapshot { Symbol = "VOO", Last = 688.11m, FetchTimestampUtc = now - StartupCoordinator.LiveQuoteFeedMaximumAge - TimeSpan.FromSeconds(1), IsStale = false }
        };
        Dictionary<string, QuoteSnapshot> boundaryQuotes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["VOO"] = new QuoteSnapshot { Symbol = "VOO", Last = 688.11m, FetchTimestampUtc = now - StartupCoordinator.LiveQuoteFeedMaximumAge, IsStale = false }
        };
        Dictionary<string, QuoteSnapshot> nearlyAgedQuotes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["VOO"] = new QuoteSnapshot { Symbol = "VOO", Last = 688.11m, FetchTimestampUtc = now - StartupCoordinator.LiveQuoteFeedMaximumAge + TimeSpan.FromSeconds(1), IsStale = false }
        };
        Dictionary<string, QuoteSnapshot> mixedAgeQuotes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["VOO"] = new QuoteSnapshot { Symbol = "VOO", Last = 688.11m, FetchTimestampUtc = now - StartupCoordinator.LiveQuoteFeedMaximumAge - TimeSpan.FromMinutes(5), IsStale = false },
            ["QUAL"] = new QuoteSnapshot { Symbol = "QUAL", Last = 215.89m, FetchTimestampUtc = now.AddMinutes(-1), IsStale = false }
        };
        Dictionary<string, QuoteSnapshot> missingTimestampQuotes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["VOO"] = new QuoteSnapshot { Symbol = "VOO", Last = 688.11m, FetchTimestampUtc = DateTimeOffset.MinValue, IsStale = false }
        };

        Assert.Equal("OFFLINE - waiting for data", StartupCoordinator.ResolveDataFreshnessText(false, emptyQuotes, now));
        Assert.Equal("LOADING - waiting for data", StartupCoordinator.ResolveDataFreshnessText(true, emptyQuotes, now));
        Assert.Equal("OFFLINE - showing last values", StartupCoordinator.ResolveDataFreshnessText(false, liveQuotes, now));
        Assert.Equal("LIVE quote feed", StartupCoordinator.ResolveDataFreshnessText(true, liveQuotes, now));
        Assert.Equal("LIVE quote feed", StartupCoordinator.ResolveDataFreshnessText(true, boundaryQuotes, now));
        Assert.Equal("LIVE quote feed", StartupCoordinator.ResolveDataFreshnessText(true, nearlyAgedQuotes, now));
        Assert.Equal("LIVE quote feed", StartupCoordinator.ResolveDataFreshnessText(true, mixedAgeQuotes, now));
        Assert.Equal("LIVE quote feed", StartupCoordinator.ResolveDataFreshnessText(true, missingTimestampQuotes, now));
        Assert.Equal("STALE - cached values present", StartupCoordinator.ResolveDataFreshnessText(true, staleQuotes, now));
        Assert.Equal("STALE - cached values present", StartupCoordinator.ResolveDataFreshnessText(true, agedQuotes, now));
        Assert.Equal(Brushes.Orange, StartupCoordinator.ResolveDataFreshnessBrush(false, liveQuotes, now));
        Assert.Equal(Brushes.Gainsboro, StartupCoordinator.ResolveDataFreshnessBrush(true, emptyQuotes, now));
        Assert.Equal(Brushes.LimeGreen, StartupCoordinator.ResolveDataFreshnessBrush(true, liveQuotes, now));
        Assert.Equal(Brushes.Goldenrod, StartupCoordinator.ResolveDataFreshnessBrush(true, staleQuotes, now));
        Assert.Equal(Brushes.Goldenrod, StartupCoordinator.ResolveDataFreshnessBrush(true, agedQuotes, now));
        Assert.True(StartupCoordinator.ResolveEffectiveDataFreshnessNetworkState(true, consecutiveQuoteFailures: 1, offlineFailureThreshold: 2));
        Assert.False(StartupCoordinator.ResolveEffectiveDataFreshnessNetworkState(true, consecutiveQuoteFailures: 2, offlineFailureThreshold: 2));
        Assert.True(StartupCoordinator.ResolveEffectiveDataFreshnessNetworkState(true, consecutiveQuoteFailures: 9, offlineFailureThreshold: 10));
        Assert.False(StartupCoordinator.ResolveEffectiveDataFreshnessNetworkState(true, consecutiveQuoteFailures: 10, offlineFailureThreshold: 10));
        Assert.False(StartupCoordinator.ResolveEffectiveDataFreshnessNetworkState(false, consecutiveQuoteFailures: 0, offlineFailureThreshold: 10));
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
