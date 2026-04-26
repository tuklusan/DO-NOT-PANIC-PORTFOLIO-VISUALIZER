using PortfolioSaver.Config.ViewModels;
using PortfolioSaver.Core.Constants;
using PortfolioSaver.Core.Models;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class TickerGroupEditorViewModelTests
{
    [Fact]
    public void Constructor_TrimsTickerCountToMaxPerTape()
    {
        TickerGroup group = new()
        {
            Name = "Tape A",
            Tickers = Enumerable.Range(1, Defaults.MaxTickersPerTape + 5)
                .Select(index => new TickerItem { Symbol = $"SYM{index}", Enabled = true })
                .ToList()
        };

        TickerGroupEditorViewModel vm = new(group);

        Assert.Equal(Defaults.MaxTickersPerTape, vm.Tickers.Count);
    }

    [Fact]
    public void ToModel_EnforcesMaxTickersPerTape()
    {
        TickerGroupEditorViewModel vm = new();
        vm.Tickers.Clear();
        foreach (int index in Enumerable.Range(1, Defaults.MaxTickersPerTape + 3))
            vm.Tickers.Add(new TickerItemEditorViewModel(new TickerItem { Symbol = $"SYM{index}", Enabled = true }));

        TickerGroup model = vm.ToModel();

        Assert.Equal(Defaults.MaxTickersPerTape, model.Tickers.Count);
    }
}
