// ============================================================================
// Copyright (c) 2026 Supratim Sanyal of SANYALnet Labs.
// Proprietary rights reserved except as expressly licensed herein.
//
// DO NOT PANIC PORTFOLIO VIEWER
// This software and its derivatives are licensed for STRICTLY NON-COMMERCIAL,
// personal, educational, or hobbyist use only. Commercial exploitation,
// corporate internal operations, or AI model training are strictly forbidden.
//
// ATTRIBUTION & DEPENDENCIES: This application incorporates the YFinance library,
// which is licensed under the Apache License, Version 2.0. A copy of the Apache
// License is provided within the distribution environment.
//
// FINANCIAL DISCLAIMER: This software is a passive visualization tool only.
// It does not provide financial, investment, legal, or tax advice. All data
// calculation and scraping outputs are provided 'AS IS' with zero guarantee
// of real-time accuracy or upstream availability.
//
// This file is subject to the terms and conditions defined in the LICENSE
// file located in the root directory of this source code repository.
// Removal or modification of this legal notice constitutes copyright infringement.
// ============================================================================
using System.Xml.Linq;
using PortfolioSaver.Shared;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class ConfigTextConsistencyTests
{
    [Fact]
    public void PortfolioVersion_UsesBeta7Labeling()
    {
        Assert.Equal("BETA-7", PortfolioVersion.BaselineLabel);
        Assert.Contains("beta7", PortfolioVersion.SemanticVersion, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BETA-7", PortfolioVersion.DisplayName, StringComparison.Ordinal);
    }

    [Fact]
    public void AboutDocument_ContainsBeta7PublisherAuthorAndLicense()
    {
        string aboutText = File.ReadAllText(Path.Combine(GetRepoRoot(), "src", "PortfolioSaver.Settings", "Content", "about.txt"));

        Assert.Contains("BETA-7 baseline", aboutText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Publisher: SANYALnet Labs", aboutText, StringComparison.Ordinal);
        Assert.Contains("Author: Supratim Sanyal", aboutText, StringComparison.Ordinal);
        Assert.Contains("License: SANYALnet Labs Non-Commercial License", aboutText, StringComparison.Ordinal);
        Assert.Contains("retires the legacy portfolio/off-hours refresh sliders", aboutText, StringComparison.Ordinal);
        Assert.Contains("uses local background files only", aboutText, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindowXaml_HasBeta7Title_AndNoBenchmarkEditorText()
    {
        string xaml = File.ReadAllText(Path.Combine(GetRepoRoot(), "src", "PortfolioSaver.Settings", "Windows", "MainWindow.xaml"));
        string configProject = File.ReadAllText(Path.Combine(GetRepoRoot(), "src", "PortfolioSaver.Config", "PortfolioSaver.Config.csproj"));
        string progressXaml = File.ReadAllText(Path.Combine(GetRepoRoot(), "src", "PortfolioSaver.Settings", "Windows", "ValidationProgressWindow.xaml"));

        Assert.Contains("Title=\"DO NOT PANIC PORTFOLIO VISUALIZER Config - BETA-7\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<ApplicationIcon>..\\PortfolioSaver.Shared\\Assets\\Branding\\dnppv-icon-rev-3.ico</ApplicationIcon>", configProject, StringComparison.Ordinal);
        Assert.DoesNotContain("Icon=\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Icon=\"", progressXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Floating benchmark cards", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Benchmark refresh", xaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MainWindowXaml_UsesRobustTabFooterAndCompactEditorLayout()
    {
        string xaml = File.ReadAllText(Path.Combine(GetRepoRoot(), "src", "PortfolioSaver.Settings", "Windows", "MainWindow.xaml"));

        Assert.Contains("x:Name=\"MainTabs\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ConfigTabItemStyle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemContainerStyle=\"{StaticResource ConfigTabItemStyle}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Background=\"#171717\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<ScrollViewer Grid.Row=\"1\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Grid.Row=\"1\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<WrapPanel Grid.Column=\"1\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ScrollViewer HorizontalScrollBarVisibility=\"Disabled\" VerticalScrollBarVisibility=\"Disabled\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"StartupShield\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Preparing configuration...", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindowXaml_UsesReadOnlyAutoFilledNames_AndOmitsUnusedTickerFields()
    {
        string xaml = File.ReadAllText(Path.Combine(GetRepoRoot(), "src", "PortfolioSaver.Settings", "Windows", "MainWindow.xaml"));

        Assert.Contains("Name fills in during Validate when available.", xaml, StringComparison.Ordinal);
        Assert.Contains("Filled automatically during Validate when available.", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("YFinance.NET symbol metadata", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding Quantity}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding CostBasis}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding Currency, UpdateSourceTrigger=PropertyChanged}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("PasswordBox", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding Settings.FinnhubApiKey", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindowFooter_HasPrimaryOkWorkflowAndValidatedCancelButton()
    {
        string xaml = File.ReadAllText(Path.Combine(GetRepoRoot(), "src", "PortfolioSaver.Settings", "Windows", "MainWindow.xaml"));

        int primaryBindingCount = xaml.Split("Content=\"{Binding PrimaryButtonText}\"", StringSplitOptions.None).Length - 1;
        Assert.Equal(1, primaryBindingCount);
        AssertValidateButtonIsOutsideNetworkLockedRegion(xaml);
        Assert.Contains("AutomationProperties.AutomationId=\"ConfigPrimaryButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"{Binding PrimaryButtonText}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"ConfigCancelButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Cancel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Cancel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{Binding ShowValidatedActionButtons, Converter={StaticResource BoolToVisibilityConverter}}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding IsValidationActionEnabled}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"ConfigStatusText\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding VersionLabel}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("FontSize=\"10\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TextTrimming=\"CharacterEllipsis\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<Menu", xaml, StringComparison.Ordinal);
    }

    private static void AssertValidateButtonIsOutsideNetworkLockedRegion(string xaml)
    {
        XDocument document = XDocument.Parse(xaml);
        XElement? validateButton = document
            .Descendants()
            .FirstOrDefault(element => element.Attributes().Any(attribute =>
                attribute.Name.LocalName.EndsWith("AutomationId", StringComparison.Ordinal) &&
                string.Equals(attribute.Value, "ConfigPrimaryButton", StringComparison.Ordinal)));

        Assert.NotNull(validateButton);
        Assert.DoesNotContain(
            validateButton!.Ancestors(),
            HasNetworkLockedIsEnabledBinding);
        Assert.Contains(
            document.Descendants(),
            HasNetworkLockedIsEnabledBinding);
    }

    private static bool HasNetworkLockedIsEnabledBinding(XElement element)
        => element.Attributes().Any(attribute =>
            string.Equals(attribute.Name.LocalName, "IsEnabled", StringComparison.Ordinal) &&
            string.Equals(attribute.Value, "{Binding IsConfigActive}", StringComparison.Ordinal));

    [Fact]
    public void ConfigApp_ForcesSoftwareRendering_ToAvoidVirtualizedTextCorruption()
    {
        string source = File.ReadAllText(Path.Combine(GetRepoRoot(), "src", "PortfolioSaver.Config", "App.xaml.cs"));
        string windowCode = File.ReadAllText(Path.Combine(GetRepoRoot(), "src", "PortfolioSaver.Settings", "Windows", "MainWindow.xaml.cs"));
        string progressXaml = File.ReadAllText(Path.Combine(GetRepoRoot(), "src", "PortfolioSaver.Settings", "Windows", "ValidationProgressWindow.xaml"));

        Assert.Contains("RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;", source, StringComparison.Ordinal);
        Assert.Contains("Software rendering enabled.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("if (TraceLog.ShouldForceSoftwareRendering())", source, StringComparison.Ordinal);
        Assert.Contains("WarmStartupSurfaceAsync()", windowCode, StringComparison.Ordinal);
        Assert.Contains("StartupShield.Visibility = Visibility.Collapsed;", windowCode, StringComparison.Ordinal);
        Assert.Contains("MainTabs.IsEnabled = false;", windowCode, StringComparison.Ordinal);
        Assert.Contains("ValidationProgressWindow", progressXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding ValidationLogText, Mode=OneWay}\"", progressXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void AdvancedTab_KeepsNewsControlsAndOmitsRuntimeArchitectureNotes()
    {
        string xaml = File.ReadAllText(Path.Combine(GetRepoRoot(), "src", "PortfolioSaver.Settings", "Windows", "MainWindow.xaml"));

        Assert.Contains("<Grid Margin=\"16\">", xaml, StringComparison.Ordinal);
        Assert.Contains("<Grid.RowDefinitions>", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"Advanced News Settings\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"News Scroller\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"AI endpoint URL:\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"AI model ID:\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"Market data runtime\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("YFinance.NET", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("per-provider", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Header=\"Service\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"Per Hour\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"Per Day\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"Single\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"Multiple\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void HelpBadges_ArePresentOnAllKeySections_WithNonEmptyTooltips()
    {
        string xaml = File.ReadAllText(Path.Combine(GetRepoRoot(), "src", "PortfolioSaver.Settings", "Windows", "MainWindow.xaml"));

        int helpBadgeCount = xaml.Split("Style=\"{StaticResource HelpBadgeStyle}\"", StringSplitOptions.None).Length - 1;
        int tooltipCount = xaml.Split("ToolTip=\"", StringSplitOptions.None).Length - 1;

        Assert.Equal(4, helpBadgeCount);
        Assert.True(tooltipCount >= 4, "Expected every visible help badge to carry a tooltip.");
        Assert.Contains("Choose your own image folder", xaml, StringComparison.Ordinal);
        Assert.Contains("Choose summarized financial news or a plain RSS feed.", xaml, StringComparison.Ordinal);
        Assert.Contains("Ticker names fill in during Validate when available.", xaml, StringComparison.Ordinal);
        Assert.Contains("Optional AI summarization requires an API key.", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void HelpAndAboutDocuments_AreBundled_NonEmpty_AndLicenseAligned()
    {
        string helpText = File.ReadAllText(Path.Combine(GetRepoRoot(), "src", "PortfolioSaver.Settings", "Content", "help.txt"));
        string aboutText = File.ReadAllText(Path.Combine(GetRepoRoot(), "src", "PortfolioSaver.Settings", "Content", "about.txt"));

        Assert.False(string.IsNullOrWhiteSpace(helpText));
        Assert.False(string.IsNullOrWhiteSpace(aboutText));
        Assert.Contains("DO NOT PANIC PORTFOLIO VISUALIZER Help", helpText, StringComparison.Ordinal);
        Assert.Contains("License: SANYALnet Labs Non-Commercial License", aboutText, StringComparison.Ordinal);
        Assert.Contains("retires the legacy portfolio/off-hours refresh sliders", aboutText, StringComparison.Ordinal);
        Assert.Contains("uses local background files only", aboutText, StringComparison.Ordinal);
        Assert.Contains("Review the bundled LICENSE file for the full license text.", helpText, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneralTab_UsesResponsiveScrollAndSharedColumns()
    {
        string xaml = File.ReadAllText(Path.Combine(GetRepoRoot(), "src", "PortfolioSaver.Settings", "Windows", "MainWindow.xaml"));

        Assert.Contains("Width=\"940\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Height=\"620\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"640\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"480\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<TabItem Header=\"General\">", xaml, StringComparison.Ordinal);
        Assert.Contains("HorizontalScrollBarVisibility=\"Disabled\"", xaml, StringComparison.Ordinal);
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SharedSizeGroup=\"GeneralLabelCol\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ColumnDefinition Width=\"*\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ColumnDefinition Width=\"1.5*\" MinWidth=\"92\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ColumnDefinition Width=\"2.2*\" MinWidth=\"132\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<TextBlock Text=\"News\" Foreground=\"White\" FontSize=\"20\" />", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("PortfolioRefreshSlider", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("OffHoursRefreshSlider", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Managed cache folder:", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Choose...\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsReadOnly=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Minimum=\"120\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void AdvancedTab_UsesStretchGridAndBoundedColumnMinimums()
    {
        string xaml = File.ReadAllText(Path.Combine(GetRepoRoot(), "src", "PortfolioSaver.Settings", "Windows", "MainWindow.xaml"));

        Assert.Contains("<TabItem Header=\"Advanced\">", xaml, StringComparison.Ordinal);
        Assert.Contains("<ScrollViewer HorizontalScrollBarVisibility=\"Disabled\" VerticalScrollBarVisibility=\"Auto\" Background=\"#171717\">", xaml, StringComparison.Ordinal);
        Assert.Contains("<RowDefinition Height=\"*\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"News Scroller\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Summarized Financial News\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"RSS Feed\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Douglas Adams\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"William Shakespeare\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"Writing style:\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"AI endpoint URL:\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"AI model ID:\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding IsSummarizedFinancialNewsSelected}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding IsRssFeedSelected}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AI API key:", xaml, StringComparison.Ordinal);
        Assert.Contains("Summarized mode requires an API key.", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("teleprinter-style", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("style-only rewriting", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("default endpoint ships", xaml, StringComparison.Ordinal);
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
