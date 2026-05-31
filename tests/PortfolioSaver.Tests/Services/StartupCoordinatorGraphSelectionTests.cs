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
}
