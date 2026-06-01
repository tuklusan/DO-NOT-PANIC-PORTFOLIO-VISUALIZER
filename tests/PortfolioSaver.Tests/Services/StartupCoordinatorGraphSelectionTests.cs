using System.Reflection;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Screensaver.Services;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class StartupCoordinatorGraphSelectionTests
{
    [Fact]
    public void SelectGraphTickerPairs_PrefersTopAbsoluteMovers_AndCapsAtSixteen()
    {
        StartupCoordinator coordinator = new();
        AppSettings settings = new()
        {
            EnableFloatingGraphs = true,
            Groups =
            [
                new TickerGroup
                {
                    Name = "Group A",
                    Enabled = true,
                    Tickers = Enumerable.Range(1, 10).Select(index => new TickerItem
                    {
                        Symbol = $"A{index:00}",
                        Enabled = true
                    }).ToList()
                },
                new TickerGroup
                {
                    Name = "Group B",
                    Enabled = true,
                    Tickers = Enumerable.Range(1, 10).Select(index => new TickerItem
                    {
                        Symbol = $"B{index:00}",
                        Enabled = true
                    }).ToList()
                }
            ]
        };

        Dictionary<string, QuoteSnapshot> quotes = new(StringComparer.OrdinalIgnoreCase);
        int score = 1;
        foreach (TickerItem ticker in settings.Groups.SelectMany(group => group.Tickers))
        {
            quotes[ticker.Symbol] = new QuoteSnapshot
            {
                Symbol = ticker.Symbol,
                Last = 100m + score,
                ChangePercent = score,
                FetchTimestampUtc = DateTimeOffset.UtcNow
            };
            score++;
        }

        coordinator.PrimeRuntimeQuotes(quotes);

        MethodInfo method = typeof(StartupCoordinator).GetMethod(
            "SelectGraphTickerPairs",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("SelectGraphTickerPairs not found.");

        object? raw = method.Invoke(coordinator, [settings]);
        IReadOnlyList<(TickerGroup Group, TickerItem Ticker)> selected =
            Assert.IsAssignableFrom<IReadOnlyList<(TickerGroup Group, TickerItem Ticker)>>(raw);

        Assert.Equal(16, selected.Count);

        HashSet<string> selectedSymbols = selected
            .Select(pair => pair.Ticker.Symbol)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.DoesNotContain("A01", selectedSymbols);
        Assert.DoesNotContain("A02", selectedSymbols);
        Assert.Contains("B10", selectedSymbols);
        Assert.Contains("B09", selectedSymbols);
        Assert.Equal(16, selectedSymbols.Count);
    }

    [Fact]
    public void TryCreateFallbackGraphSnapshot_UsesQuoteMemoryWhenHistoryIsUnavailable()
    {
        StartupCoordinator coordinator = new();
        coordinator.PrimeRuntimeQuotes(new Dictionary<string, QuoteSnapshot>(StringComparer.OrdinalIgnoreCase)
        {
            ["VOO"] = new QuoteSnapshot
            {
                Symbol = "VOO",
                Last = 512.34m,
                PreviousClose = 509.11m,
                FetchTimestampUtc = DateTimeOffset.UtcNow
            }
        });

        MethodInfo method = typeof(StartupCoordinator).GetMethod(
            "TryCreateFallbackGraphSnapshot",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("TryCreateFallbackGraphSnapshot not found.");

        object?[] args = ["VOO", 1, null];
        bool created = Assert.IsType<bool>(method.Invoke(coordinator, args));
        Assert.True(created);

        TickerHistorySnapshot snapshot = Assert.IsType<TickerHistorySnapshot>(args[2]);
        Assert.Equal("VOO", snapshot.Symbol);
        Assert.Equal(1, snapshot.LookbackDays);
        Assert.Equal(2, snapshot.Points.Count);
        Assert.Equal(509.11m, snapshot.Points[0].Close);
        Assert.Equal(512.34m, snapshot.Points[1].Close);
    }
}
