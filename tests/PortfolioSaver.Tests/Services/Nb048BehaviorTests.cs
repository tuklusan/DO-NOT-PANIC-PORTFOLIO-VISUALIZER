using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class Nb048BehaviorTests
{
    [Fact]
    public void StartupCoordinator_BuildSceneAsync_UsesCachedNewsInsteadOfLiveNewsTask()
    {
        string source = File.ReadAllText(Path.Combine(GetRepoRoot(), "src", "PortfolioSaver.Presentation", "Services", "StartupCoordinator.cs"));

        Assert.Contains("IReadOnlyList<string> headlines = _financeNewsService.GetCachedHeadlines(settings.NewsScrollerMode);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Task<IReadOnlyList<string>> headlinesTask = _financeNewsService.GetHeadlinesAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("await Task.WhenAll(quotesTask, backgroundsTask, headlinesTask);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ScreensaverSceneControl_UsesIndependentBackgroundNewsRefreshLane()
    {
        string source = File.ReadAllText(Path.Combine(GetRepoRoot(), "src", "PortfolioSaver.Presentation", "Controls", "ScreensaverSceneControl.xaml.cs"));

        Assert.Contains("StartNewsRefreshLoop();", source, StringComparison.Ordinal);
        Assert.Contains("RunNewsRefreshLoopAsync", source, StringComparison.Ordinal);
        Assert.Contains("RefreshNewsLaneAsync", source, StringComparison.Ordinal);
        Assert.Contains("\"NewsUiPatchComplete\"", source, StringComparison.Ordinal);
        Assert.Contains("SyncNews(refreshedNews);", source, StringComparison.Ordinal);
    }

    private static string GetRepoRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "PortfolioScreensaver.sln")))
                return current.FullName;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("PortfolioScreensaver.sln not found from test base directory.");
    }
}
