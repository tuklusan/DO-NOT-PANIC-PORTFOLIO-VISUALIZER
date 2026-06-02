using PortfolioSaver.Shared;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class ConfigTextConsistencyTests
{
    [Fact]
    public void PortfolioVersion_UsesBeta6Labeling()
    {
        Assert.Equal("BETA-6", PortfolioVersion.BaselineLabel);
        Assert.Contains("beta6", PortfolioVersion.SemanticVersion, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BETA-6", PortfolioVersion.DisplayName, StringComparison.Ordinal);
    }

    [Fact]
    public void AboutDocument_ContainsBeta6PublisherAuthorAndLicense()
    {
        string aboutText = File.ReadAllText(Path.Combine(GetRepoRoot(), "src", "PortfolioSaver.Settings", "Content", "about.txt"));

        Assert.Contains("BETA-6 baseline", aboutText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Publisher: SANYALnet Labs", aboutText, StringComparison.Ordinal);
        Assert.Contains("Author: Supratim Sanyal", aboutText, StringComparison.Ordinal);
        Assert.Contains("License: MIT License", aboutText, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindowXaml_HasBeta6Title_AndNoBenchmarkEditorText()
    {
        string xaml = File.ReadAllText(Path.Combine(GetRepoRoot(), "src", "PortfolioSaver.Settings", "Windows", "MainWindow.xaml"));

        Assert.Contains("Title=\"DO NOT PANIC PORTFOLIO VISUALIZER Config - BETA-6\"", xaml, StringComparison.Ordinal);
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

        Assert.Contains("Name fills in during Validate using YFinance.NET symbol metadata.", xaml, StringComparison.Ordinal);
        Assert.Contains("Filled automatically during Validate from YFinance.NET metadata.", xaml, StringComparison.Ordinal);
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
    public void AdvancedTab_RetiresOldDataSourceGridAndExplainsFixedYFinanceRuntime()
    {
        string xaml = File.ReadAllText(Path.Combine(GetRepoRoot(), "src", "PortfolioSaver.Settings", "Windows", "MainWindow.xaml"));

        Assert.Contains("<Grid Margin=\"16\">", xaml, StringComparison.Ordinal);
        Assert.Contains("<Grid.RowDefinitions>", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"Advanced Runtime and News\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"Market data runtime\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Portfolio, macro, and graph retrieval now run exclusively through YFinance.NET.", xaml, StringComparison.Ordinal);
        Assert.Contains("The old per-provider API key and policy grid has been intentionally retired from the config surface.", xaml, StringComparison.Ordinal);
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

        Assert.Equal(5, helpBadgeCount);
        Assert.True(tooltipCount >= 5, "Expected every visible help badge to carry a tooltip.");
        Assert.Contains("Market data now runs through YFinance.NET only.", xaml, StringComparison.Ordinal);
        Assert.Contains("Summarized Financial News uses the DeepSeek API key from the config screen, protected local secret storage, or environment overrides", xaml, StringComparison.Ordinal);
        Assert.Contains("Managed exchange photos are cached under Local AppData.", xaml, StringComparison.Ordinal);
        Assert.Contains("Ticker names auto-fill during Apply when validation can resolve them.", xaml, StringComparison.Ordinal);
        Assert.Contains("Advanced settings now cover the news scroller and the fixed YFinance.NET runtime profile.", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void HelpAndAboutDocuments_AreBundled_NonEmpty_AndLicenseAligned()
    {
        string helpText = File.ReadAllText(Path.Combine(GetRepoRoot(), "src", "PortfolioSaver.Settings", "Content", "help.txt"));
        string aboutText = File.ReadAllText(Path.Combine(GetRepoRoot(), "src", "PortfolioSaver.Settings", "Content", "about.txt"));

        Assert.False(string.IsNullOrWhiteSpace(helpText));
        Assert.False(string.IsNullOrWhiteSpace(aboutText));
        Assert.Contains("DO NOT PANIC PORTFOLIO VISUALIZER Help", helpText, StringComparison.Ordinal);
        Assert.Contains("License: MIT License", aboutText, StringComparison.Ordinal);
        Assert.Contains("Official License URL: https://opensource.org/license/mit/", aboutText, StringComparison.Ordinal);
        Assert.Contains("Review the bundled LICENSE file or the official MIT License page for the full license text.", helpText, StringComparison.Ordinal);
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
        Assert.Contains("Text=\"DeepSeek style:\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"DeepSeek endpoint URL:\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"DeepSeek model ID:\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding IsSummarizedFinancialNewsSelected}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding IsRssFeedSelected}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("DeepSeek API key:", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"Market data runtime\"", xaml, StringComparison.Ordinal);
        Assert.Contains("One-by-one work is paced at 1 second when batching is not available, and live cache/metadata freshness is capped at 10 minutes.", xaml, StringComparison.Ordinal);
        Assert.Contains("The old per-provider API key and policy grid has been intentionally retired from the config surface.", xaml, StringComparison.Ordinal);
        Assert.Contains("teleprinter-style band with uppercase courier text, typed character by character, paused, cleared, and only scrolled when the item is too long", xaml, StringComparison.Ordinal);
        Assert.Contains("style-only rewriting", xaml, StringComparison.Ordinal);
        Assert.Contains("The three-field contract is API key, endpoint URL, and model ID.", xaml, StringComparison.Ordinal);
        Assert.Contains("default endpoint ships as https://api.deepseek.com/v1", xaml, StringComparison.Ordinal);
        Assert.Contains("default model ID ships as deepseek-v4-flash", xaml, StringComparison.Ordinal);
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



