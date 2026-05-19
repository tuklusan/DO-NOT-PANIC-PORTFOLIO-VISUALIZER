using System.Windows;
using System.Windows.Automation;

namespace PortfolioSaver.Config.Windows;

public partial class ValidationProgressWindow : Window
{
    public ValidationProgressWindow()
    {
        InitializeComponent();
        AutomationProperties.SetAutomationId(this, "ValidationProgressWindow");
        AutomationProperties.SetName(this, Title);
    }

    private void OnLogTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        LogTextBox.CaretIndex = LogTextBox.Text.Length;
        LogTextBox.ScrollToEnd();
    }
}
