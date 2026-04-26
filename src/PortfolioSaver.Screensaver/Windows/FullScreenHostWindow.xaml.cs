using System.Windows;
using System.Windows.Automation;
using PortfolioSaver.Screensaver.Services;
using PortfolioSaver.Shared;

namespace PortfolioSaver.Screensaver.Windows;

public partial class FullScreenHostWindow : Window
{
    public FullScreenHostWindow()
    {
        InitializeComponent();
        Title = $"Portfolio Screensaver {PortfolioVersion.SemanticVersion}";
        AutomationProperties.SetAutomationId(this, "ScreensaverHostWindow");
        AutomationProperties.SetName(this, Title);
        AutomationProperties.SetHelpText(this, PortfolioVersion.SemanticVersion);

        InputExitMonitor inputExitMonitor = new(this);
        inputExitMonitor.Attach();
    }
}
