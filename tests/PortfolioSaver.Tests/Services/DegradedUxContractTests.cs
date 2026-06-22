using PortfolioSaver.Core.Models;
using PortfolioSaver.Render.ViewModels;
using PortfolioSaver.Screensaver.Services;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class DegradedUxContractTests
{
    [Fact]
    public void ContractDocument_DefinesAccessibleStatesPlaceholdersAndResponsiveControls()
    {
        string contract = ReadRepoText("docs", "DEGRADED_UX_CONTRACT.md");

        Assert.Contains("RuntimeDataFreshnessText", contract, StringComparison.Ordinal);
        Assert.Contains("OFFLINE - showing last values", contract, StringComparison.Ordinal);
        Assert.Contains("STALE - cached values present", contract, StringComparison.Ordinal);
        Assert.Contains("LOADING - waiting for data", contract, StringComparison.Ordinal);
        Assert.Contains("stable placeholder `--`", contract, StringComparison.Ordinal);
        Assert.Contains("Cancel and Escape must close config dialogs promptly", contract, StringComparison.Ordinal);
        Assert.Contains("must not rely on color alone", contract, StringComparison.Ordinal);
        Assert.Contains("must avoid implementation details", contract, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeFreshnessBehavior_UsesContractedAccessibleTextStates()
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

        Assert.Equal("OFFLINE - waiting for data", StartupCoordinator.ResolveDataFreshnessText(false, emptyQuotes, now));
        Assert.Equal("LOADING - waiting for data", StartupCoordinator.ResolveDataFreshnessText(true, emptyQuotes, now));
        Assert.Equal("OFFLINE - showing last values", StartupCoordinator.ResolveDataFreshnessText(false, liveQuotes, now));
        Assert.Equal("LIVE quote feed", StartupCoordinator.ResolveDataFreshnessText(true, liveQuotes, now));
        Assert.Equal("STALE - cached values present", StartupCoordinator.ResolveDataFreshnessText(true, staleQuotes, now));
        Assert.Equal("STALE - cached values present", StartupCoordinator.ResolveDataFreshnessText(true, agedQuotes, now));
    }

    [Fact]
    public void PlaceholderViewModels_DefaultToStableDashPlaceholder()
    {
        FloatingGraphViewModel graph = new();
        MacroMeterViewModel macro = new();
        ClockCityViewModel worldMarket = new();

        Assert.Equal("--", graph.LastText);
        Assert.Equal("--", graph.ChangeText);
        Assert.Equal("--", macro.ValueText);
        Assert.Equal("--", macro.ChangeText);
        Assert.Equal("--", worldMarket.IndexValueText);
        Assert.Equal("--", worldMarket.IndexChangeText);
    }

    [Fact]
    public void PublicAutomationIds_RemainAvailableForAccessibilityAndHarnessInspection()
    {
        string statusBarXaml = ReadRepoText("src", "PortfolioSaver.Render", "Controls", "StatusBarControl.xaml");
        string settingsXaml = ReadRepoText("src", "PortfolioSaver.Settings", "Windows", "MainWindow.xaml");

        Assert.Contains("AutomationProperties.AutomationId=\"RuntimeDataFreshnessText\"", statusBarXaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"{Binding DataFreshnessText}\"", statusBarXaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"ConfigStatusText\"", settingsXaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"ConfigPrimaryButton\"", settingsXaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"ConfigCancelButton\"", settingsXaml, StringComparison.Ordinal);
        Assert.Contains("IsCancel=\"True\"", settingsXaml, StringComparison.Ordinal);
    }

    private static string ReadRepoText(params string[] relativeParts)
    {
        string repoRoot = GetRepoRoot();
        string path = Path.Combine(repoRoot, Path.Combine(relativeParts));
        if (!File.Exists(path))
            throw new FileNotFoundException($"Required degraded UX contract input was not found. RepoRoot='{repoRoot}', RelativePath='{string.Join("/", relativeParts)}'.", path);

        return File.ReadAllText(path);
    }

    private static string GetRepoRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        string? start = current?.FullName;
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "PortfolioScreensaver.sln")))
                return current.FullName;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException($"Could not locate repo root from test AppContext.BaseDirectory '{start}'.");
    }
}
