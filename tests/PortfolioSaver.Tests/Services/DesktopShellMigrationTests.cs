// ============================================================================
// Copyright (c) 2026 Supratim Sanyal of SANYALnet Labs.
// Proprietary rights reserved except as expressly licensed herein.
//
// DO NOT PANIC PORTFOLIO VISUALIZER
// This file is governed by the SANYALnet Labs Non-Commercial License in the
// root LICENSE file. Non-Commercial use is permitted; Commercial Use and use
// for AI/ML model training are prohibited unless separately authorized.
//
// Attribution is required: "Based on original work by Supratim Sanyal of
// SANYALnet Labs." See LICENSE for full terms, warranty disclaimer, termination,
// patent, trademark, and governing-law provisions.
// ============================================================================
using Xunit;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using PortfolioSaver.Desktop.Windows;

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
        Assert.Contains("<ApplicationIcon>..\\PortfolioSaver.Shared\\Assets\\Branding\\dnppv-icon-rev-3.ico</ApplicationIcon>", csproj, StringComparison.Ordinal);
        Assert.Contains("<Resource Include=\"..\\PortfolioSaver.Shared\\Assets\\Branding\\dnppv-icon-rev-3.ico\" Link=\"Assets\\Branding\\dnppv-icon-rev-3.ico\" />", csproj, StringComparison.Ordinal);
        Assert.Contains("<Resource Include=\"..\\PortfolioSaver.Shared\\Assets\\Branding\\dnppv-icon-rev-3-splash.png\" Link=\"Assets\\Branding\\dnppv-icon-rev-3-splash.png\" />", csproj, StringComparison.Ordinal);
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
        Assert.Contains("<Menu x:Name=\"MainMenu\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"_Options\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"OptionsMenuRoot\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"_Settings\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"_Help\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"_About\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"ViewFullScreenMenuItem\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"OptionsSettingsMenuItem\"", xaml, StringComparison.Ordinal);
        Assert.Contains("PortfolioSaver.Presentation", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SceneHost\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Icon=\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Width=\"1180\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Height=\"720\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"1180\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"720\"", xaml, StringComparison.Ordinal);
        Assert.Contains("WindowState=\"Maximized\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SizeChanged=\"OnWindowSizeChanged\"", xaml, StringComparison.Ordinal);
        Assert.Contains("StateChanged=\"OnWindowStateChanged\"", xaml, StringComparison.Ordinal);
        Assert.Contains("PreviewMouseDoubleClick=\"OnWindowPreviewMouseDoubleClick\"", xaml, StringComparison.Ordinal);
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
        Assert.Contains("OnWindowPreviewMouseDoubleClick", code, StringComparison.Ordinal);
        Assert.Contains("ShouldToggleFullScreenFromDoubleClick", code, StringComparison.Ordinal);
        Assert.Contains("ShouldSuppressDoubleClickFullScreenForInteractiveSource", code, StringComparison.Ordinal);
        Assert.Contains("Leave the routed event unhandled", code, StringComparison.Ordinal);
        Assert.Contains("ToggleFullScreen();", code, StringComparison.Ordinal);
        Assert.Contains("e.Key == Key.F11", code, StringComparison.Ordinal);
        Assert.Contains("e.Handled = true;", code, StringComparison.Ordinal);
        Assert.Contains("GetCurrentMonitorBoundsInDips()", code, StringComparison.Ordinal);
        Assert.Contains("MonitorFromWindow(hwnd, MonitorDefaultToNearest)", code, StringComparison.Ordinal);
        Assert.Contains("GetMonitorInfo(monitor, ref monitorInfo)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Windows.Forms.Screen", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SystemParameters.VirtualScreenWidth", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SystemParameters.VirtualScreenHeight", code, StringComparison.Ordinal);
        Assert.Contains("private const double RestoredWindowWidth = 1180d;", code, StringComparison.Ordinal);
        Assert.Contains("private const double RestoredWindowHeight = 720d;", code, StringComparison.Ordinal);
        Assert.Contains("ApplyWindowStateConstraints()", code, StringComparison.Ordinal);
        Assert.Contains("EnforceRestoredWindowSize()", code, StringComparison.Ordinal);
        Assert.Contains("ApplyFullScreenBoundsIfNeeded", code, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.ApplicationIdle", code, StringComparison.Ordinal);
        Assert.Contains("MaxWidth = double.PositiveInfinity;", code, StringComparison.Ordinal);
        Assert.Contains("MaxHeight = double.PositiveInfinity;", code, StringComparison.Ordinal);
        Assert.DoesNotContain("MaxWidth = SystemParameters.WorkArea.Width;", code, StringComparison.Ordinal);
        Assert.DoesNotContain("MaxHeight = SystemParameters.WorkArea.Height;", code, StringComparison.Ordinal);
        Assert.Contains("MainMenu.Visibility = Visibility.Collapsed;", code, StringComparison.Ordinal);
        Assert.Contains("MainMenu.Visibility = Visibility.Visible;", code, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetAutomationId(this, \"DesktopMainWindow\")", code, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetHelpText(this, PortfolioVersion.SemanticVersion)", code, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetName(OptionsMenuItem, \"Options\")", code, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetName(SettingsMenuItem, \"Settings\")", code, StringComparison.Ordinal);
        Assert.Contains("window.ValidationActivityChanged += OnValidationActivityChanged;", code, StringComparison.Ordinal);
        Assert.Contains("if (isValidating)", code, StringComparison.Ordinal);
        Assert.Contains("SceneHost?.SetValidationPause(true);", code, StringComparison.Ordinal);
        Assert.Contains("SceneHost?.SetValidationPause(false);", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SceneHost?.SetValidationPause(isValidating);", code, StringComparison.Ordinal);
        Assert.Contains("AboutWindow window = new()", code, StringComparison.Ordinal);
        Assert.Contains("window.ShowDialog();", code, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopShell_DoubleClickToggleDecision_RequiresLeftButtonAwayFromMenu()
    {
        Assert.True(MainWindow.ShouldToggleFullScreenFromDoubleClick(MouseButton.Left, isMenuMouseOver: false));
        Assert.False(MainWindow.ShouldToggleFullScreenFromDoubleClick(MouseButton.Left, isMenuMouseOver: true));

        foreach (MouseButton button in new[] { MouseButton.Right, MouseButton.Middle, MouseButton.XButton1, MouseButton.XButton2 })
        {
            Assert.False(MainWindow.ShouldToggleFullScreenFromDoubleClick(button, isMenuMouseOver: false));
            Assert.False(MainWindow.ShouldToggleFullScreenFromDoubleClick(button, isMenuMouseOver: true));
        }
    }

    [Fact]
    public void DesktopShell_DoubleClickSuppression_CoversKnownInteractiveControlsOnly()
    {
        RunOnSta(() =>
        {
            Assert.True(MainWindow.ShouldSuppressDoubleClickFullScreenForInteractiveSource(new MenuItem()));
            Assert.True(MainWindow.ShouldSuppressDoubleClickFullScreenForInteractiveSource(new Button()));
            Assert.True(MainWindow.ShouldSuppressDoubleClickFullScreenForInteractiveSource(new TextBox()));
            Assert.True(MainWindow.ShouldSuppressDoubleClickFullScreenForInteractiveSource(new ListBox()));
            Assert.True(MainWindow.ShouldSuppressDoubleClickFullScreenForInteractiveSource(new Slider()));
            Assert.True(MainWindow.ShouldSuppressDoubleClickFullScreenForInteractiveSource(new Thumb()));
            Assert.True(MainWindow.ShouldSuppressDoubleClickFullScreenForInteractiveSource(new PasswordBox()));
            Assert.True(MainWindow.ShouldSuppressDoubleClickFullScreenForInteractiveSource(new TreeViewItem()));
            Assert.False(MainWindow.ShouldSuppressDoubleClickFullScreenForInteractiveSource(new Border()));
            Assert.False(MainWindow.ShouldSuppressDoubleClickFullScreenForInteractiveSource(null));
        });
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
            throw failure;
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
    public void DesktopApp_CanLaunchDirectlyIntoFullScreenViaStartupArgument()
    {
        string appXaml = File.ReadAllText(Path.Combine(GetRepoRoot(), "src", "PortfolioSaver.Desktop", "App.xaml"));
        string appCode = File.ReadAllText(Path.Combine(GetRepoRoot(), "src", "PortfolioSaver.Desktop", "App.xaml.cs"));

        Assert.DoesNotContain("StartupUri=", appXaml, StringComparison.Ordinal);
        Assert.Contains("bool startFullScreen = e.Args.Any(arg => string.Equals(arg, \"--fullscreen\", StringComparison.OrdinalIgnoreCase));", appCode, StringComparison.Ordinal);
        Assert.Contains("var window = new MainWindow();", appCode, StringComparison.Ordinal);
        Assert.Contains("new Action(window.EnterFullScreen)", appCode, StringComparison.Ordinal);
        Assert.Contains("MainWindow = window;", appCode, StringComparison.Ordinal);
        Assert.Contains("window.Show();", appCode, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopApp_AiNewsStartupCheck_IsNonModalAndAllowsRefreshRetries()
    {
        string appCode = File.ReadAllText(Path.Combine(GetRepoRoot(), "src", "PortfolioSaver.Desktop", "App.xaml.cs"));

        Assert.Contains("CheckConfiguredAiNewsAccessAsync", appCode, StringComparison.Ordinal);
        Assert.Contains("QueueConfiguredAiNewsAccessCheck", appCode, StringComparison.Ordinal);
        Assert.Contains("AiNewsStartupProbeTimeout = TimeSpan.FromSeconds(15)", appCode, StringComparison.Ordinal);
        Assert.Contains("summarized news will retry on the normal refresh cadence", appCode, StringComparison.Ordinal);
        int showIndex = appCode.IndexOf("window.Show();", StringComparison.Ordinal);
        int probeIndex = appCode.IndexOf("QueueConfiguredAiNewsAccessCheck();", StringComparison.Ordinal);
        Assert.NotEqual(-1, showIndex);
        Assert.NotEqual(-1, probeIndex);
        Assert.True(
            showIndex < probeIndex,
            "AI availability probing must be queued only after the main window is visible.");
        Assert.DoesNotContain("ForceRssNewsForCurrentSession", appCode, StringComparison.Ordinal);
        Assert.DoesNotContain("AI summarized financial news is not available right now", appCode, StringComparison.Ordinal);
        Assert.DoesNotContain("AI summarized financial news could not be verified", appCode, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyScreensaverHost_UsesPresentationAssembly()
    {
        string fullScreenXaml = File.ReadAllText(Path.Combine(GetRepoRoot(), "src", "PortfolioSaver.Screensaver", "Windows", "FullScreenHostWindow.xaml"));
        string previewXaml = File.ReadAllText(Path.Combine(GetRepoRoot(), "src", "PortfolioSaver.Screensaver", "Windows", "PreviewHostWindow.xaml"));
        string csproj = File.ReadAllText(Path.Combine(GetRepoRoot(), "src", "PortfolioSaver.Screensaver", "PortfolioSaver.Screensaver.csproj"));

        Assert.Contains("assembly=PortfolioSaver.Presentation", fullScreenXaml, StringComparison.Ordinal);
        Assert.Contains("assembly=PortfolioSaver.Presentation", previewXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Icon=\"", fullScreenXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Icon=\"", previewXaml, StringComparison.Ordinal);
        Assert.Contains("<ApplicationIcon>..\\PortfolioSaver.Shared\\Assets\\Branding\\dnppv-icon-rev-3.ico</ApplicationIcon>", csproj, StringComparison.Ordinal);
        Assert.Contains("<Resource Include=\"..\\PortfolioSaver.Shared\\Assets\\Branding\\dnppv-icon-rev-3.ico\" Link=\"Assets\\Branding\\dnppv-icon-rev-3.ico\" />", csproj, StringComparison.Ordinal);
        Assert.Contains("PortfolioSaver.Presentation", csproj, StringComparison.Ordinal);
    }

    [Fact]
    public void AboutWindow_UsesBrandSplashAndPublisherMetadata()
    {
        string xaml = File.ReadAllText(Path.Combine(GetRepoRoot(), "src", "PortfolioSaver.Desktop", "Windows", "AboutWindow.xaml"));
        string code = File.ReadAllText(Path.Combine(GetRepoRoot(), "src", "PortfolioSaver.Desktop", "Windows", "AboutWindow.xaml.cs"));

        Assert.Contains("Source=\"/Assets/Branding/dnppv-icon-rev-3-splash.png\"", xaml, StringComparison.Ordinal);
        Assert.Contains("DO NOT PANIC PORTFOLIO VISUALIZER", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("BETA-7 desktop baseline", xaml, StringComparison.Ordinal);
        Assert.Contains("Publisher: {AppIdentity.PublisherName}", code, StringComparison.Ordinal);
        Assert.Contains("Author: {AppIdentity.AuthorName}", code, StringComparison.Ordinal);
        Assert.Contains("License: {AppIdentity.LicenseName}", code, StringComparison.Ordinal);
        Assert.Contains("FullLicenseText", code, StringComparison.Ordinal);
        Assert.Contains("License Text", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding FullLicenseText}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("decorative desktop market visualizer", xaml, StringComparison.Ordinal);
        Assert.Contains("absolutely must not be used as a financial planning", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("The revision-3 brand assets are now", xaml, StringComparison.Ordinal);
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
