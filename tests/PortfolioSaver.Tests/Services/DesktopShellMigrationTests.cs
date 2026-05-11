using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class DesktopShellMigrationTests
{
    [Fact]
    public void GlobalJson_PinsDotNet10Sdk()
    {
        string json = File.ReadAllText(Path.Combine(GetRepoRoot(), "global.json"));

        Assert.Contains("\"version\": \"10.0.201\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopProject_Exists_AndTargetsNet10()
    {
        string csproj = File.ReadAllText(Path.Combine(GetRepoRoot(), "src", "PortfolioSaver.Desktop", "PortfolioSaver.Desktop.csproj"));

        Assert.Contains("<TargetFramework>net10.0-windows</TargetFramework>", csproj, StringComparison.Ordinal);
        Assert.Contains("PortfolioSaver.Presentation", csproj, StringComparison.Ordinal);
        Assert.Contains("PortfolioSaver.Settings", csproj, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopShell_DefinesRequiredMenuItems()
    {
        string xaml = File.ReadAllText(Path.Combine(GetRepoRoot(), "src", "PortfolioSaver.Desktop", "Windows", "MainWindow.xaml"));

        Assert.Contains("Header=\"_File\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"E_xit\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"_View\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"_Full Screen\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"_Options\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"_Settings\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"_Help\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"_About\"", xaml, StringComparison.Ordinal);
        Assert.Contains("PortfolioSaver.Presentation", xaml, StringComparison.Ordinal);
        Assert.Contains("Width=\"1180\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Height=\"720\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopShell_ImplementsFullScreenToggle_AndEscExit()
    {
        string code = File.ReadAllText(Path.Combine(GetRepoRoot(), "src", "PortfolioSaver.Desktop", "Windows", "MainWindow.xaml.cs"));

        Assert.Contains("ToggleFullScreen()", code, StringComparison.Ordinal);
        Assert.Contains("EnterFullScreen()", code, StringComparison.Ordinal);
        Assert.Contains("ExitFullScreen()", code, StringComparison.Ordinal);
        Assert.Contains("if (e.Key == Key.F11)", code, StringComparison.Ordinal);
        Assert.Contains("if (e.Key == Key.Escape && _isFullScreen)", code, StringComparison.Ordinal);
        Assert.Contains("SystemParameters.VirtualScreenWidth", code, StringComparison.Ordinal);
        Assert.Contains("SystemParameters.VirtualScreenHeight", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigHost_IsThinLauncher_UsingSharedSettingsWindow()
    {
        string csproj = File.ReadAllText(Path.Combine(GetRepoRoot(), "src", "PortfolioSaver.Config", "PortfolioSaver.Config.csproj"));
        string appXaml = File.ReadAllText(Path.Combine(GetRepoRoot(), "src", "PortfolioSaver.Config", "App.xaml"));
        string appCode = File.ReadAllText(Path.Combine(GetRepoRoot(), "src", "PortfolioSaver.Config", "App.xaml.cs"));

        Assert.Contains("PortfolioSaver.Settings", csproj, StringComparison.Ordinal);
        Assert.DoesNotContain("StartupUri=", appXaml, StringComparison.Ordinal);
        Assert.Contains("var window = new MainWindow();", appCode, StringComparison.Ordinal);
        Assert.Contains("window.Show();", appCode, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyScreensaverHost_UsesPresentationAssembly()
    {
        string fullScreenXaml = File.ReadAllText(Path.Combine(GetRepoRoot(), "src", "PortfolioSaver.Screensaver", "Windows", "FullScreenHostWindow.xaml"));
        string previewXaml = File.ReadAllText(Path.Combine(GetRepoRoot(), "src", "PortfolioSaver.Screensaver", "Windows", "PreviewHostWindow.xaml"));
        string csproj = File.ReadAllText(Path.Combine(GetRepoRoot(), "src", "PortfolioSaver.Screensaver", "PortfolioSaver.Screensaver.csproj"));

        Assert.Contains("assembly=PortfolioSaver.Presentation", fullScreenXaml, StringComparison.Ordinal);
        Assert.Contains("assembly=PortfolioSaver.Presentation", previewXaml, StringComparison.Ordinal);
        Assert.Contains("PortfolioSaver.Presentation", csproj, StringComparison.Ordinal);
    }

    private static string GetRepoRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, "PortfolioScreensaver.sln");
            if (File.Exists(candidate))
                return current.FullName;

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from test base directory.");
    }
}
