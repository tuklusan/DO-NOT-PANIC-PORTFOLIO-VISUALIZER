using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class Nb049BehaviorTests
{
    [Fact]
    public void ScreensaverSceneControl_UsesIndependentMacroRefreshLane()
    {
        string source = File.ReadAllText(Path.Combine(GetRepoRoot(), "src", "PortfolioSaver.Presentation", "Controls", "ScreensaverSceneControl.xaml.cs"));

        Assert.Contains("StartMacroLane();", source, StringComparison.Ordinal);
        Assert.Contains("RunMacroLaneAsync", source, StringComparison.Ordinal);
        Assert.Contains("BuildMacroLaneSnapshotAsync", source, StringComparison.Ordinal);
        Assert.Contains("ApplyMacroLaneSnapshot", source, StringComparison.Ordinal);
        Assert.Contains("MacroLaneMinimumRefreshInterval", source, StringComparison.Ordinal);
        Assert.Contains("TimeSpan remaining = MacroLaneMinimumRefreshInterval", source, StringComparison.Ordinal);
        Assert.Contains("\"MacroRefreshStart\"", source, StringComparison.Ordinal);
        Assert.Contains("\"MacroUiPatchComplete\"", source, StringComparison.Ordinal);
        Assert.Contains("QueueMacroRefresh(\"quote-delta\");", source, StringComparison.Ordinal);
        Assert.Contains("IReadOnlyDictionary<string, QuoteSnapshot> previousQuotes = _latestQuotes;", source, StringComparison.Ordinal);
        Assert.Contains("HasMeaningfulMacroDelta(previousQuotes, deltaQuotes)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateStatusMacroMeters(force: true);", source, StringComparison.Ordinal);
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
