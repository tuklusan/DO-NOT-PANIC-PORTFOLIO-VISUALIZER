using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class Nb051BehaviorTests
{
    [Fact]
    public void ScreensaverSceneControl_UsesIndependentWorldMarketsLane()
    {
        string source = File.ReadAllText(Path.Combine(GetRepoRoot(), "src", "PortfolioSaver.Presentation", "Controls", "ScreensaverSceneControl.xaml.cs"));

        Assert.Contains("StartWorldMarketsLane();", source, StringComparison.Ordinal);
        Assert.Contains("RunWorldMarketsLaneAsync", source, StringComparison.Ordinal);
        Assert.Contains("BuildWorldMarketsLaneSnapshotAsync", source, StringComparison.Ordinal);
        Assert.Contains("ApplyWorldMarketsLaneSnapshot", source, StringComparison.Ordinal);
        Assert.Contains("WorldMarketsLaneMinimumRefreshInterval", source, StringComparison.Ordinal);
        Assert.Contains("TimeSpan remaining = WorldMarketsLaneMinimumRefreshInterval", source, StringComparison.Ordinal);
        Assert.Contains("\"WorldMarketsRefreshStart\"", source, StringComparison.Ordinal);
        Assert.Contains("\"WorldMarketsFetchComplete\"", source, StringComparison.Ordinal);
        Assert.Contains("\"WorldMarketsMergeComplete\"", source, StringComparison.Ordinal);
        Assert.Contains("\"WorldMarketsUiPatchComplete\"", source, StringComparison.Ordinal);
        Assert.Contains("QueueWorldMarketsRefresh(refreshAncillary: false, reason: \"quote-delta\");", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyClockMarketData(force: false)", source, StringComparison.Ordinal);
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
