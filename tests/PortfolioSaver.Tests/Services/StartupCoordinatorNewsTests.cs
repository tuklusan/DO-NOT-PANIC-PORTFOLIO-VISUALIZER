using System.Reflection;
using PortfolioSaver.Render.ViewModels;
using PortfolioSaver.Screensaver.Services;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class StartupCoordinatorNewsTests
{
    [Fact]
    public void BuildNews_RepeatsHeadlinesToMinimumCount()
    {
        NewsFlasherViewModel news = InvokeBuildNews(["Headline A", "Headline B"]);

        Assert.True(news.Headlines.Count >= 20);
        Assert.All(news.Headlines, headline => Assert.False(string.IsNullOrWhiteSpace(headline.Text)));
        Assert.Contains("Headline A", news.MarqueeText, StringComparison.Ordinal);
        Assert.Contains("Headline B", news.MarqueeText, StringComparison.Ordinal);
        Assert.Contains(" | ", news.MarqueeText, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildNews_WhenInputEmpty_UsesWaitingMessage()
    {
        NewsFlasherViewModel news = InvokeBuildNews([]);

        Assert.True(news.Headlines.Count >= 1);
        Assert.Contains(news.Headlines, headline =>
            headline.Text.Contains("Waiting for Yahoo Finance headlines", StringComparison.OrdinalIgnoreCase));
    }

    private static NewsFlasherViewModel InvokeBuildNews(IReadOnlyList<string> headlines)
    {
        MethodInfo? method = typeof(StartupCoordinator).GetMethod(
            "BuildNews",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        object? value = method!.Invoke(null, [headlines]);
        return Assert.IsType<NewsFlasherViewModel>(value);
    }
}
