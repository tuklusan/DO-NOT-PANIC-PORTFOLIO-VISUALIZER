using System.Windows;
using System.Windows.Automation;
using System.Windows.Threading;
using PortfolioSaver.Config.ViewModels;
using PortfolioSaver.Shared;

namespace PortfolioSaver.Config.Windows;

public partial class MainWindow : Window
{
    private bool _startupPrepared;

    public MainWindow()
    {
        InitializeComponent();
        AutomationProperties.SetAutomationId(this, "ConfigMainWindow");
        AutomationProperties.SetName(this, Title);
        AutomationProperties.SetHelpText(this, PortfolioVersion.SemanticVersion);

        if (DataContext is MainWindowViewModel viewModel)
            viewModel.CloseRequested += OnCloseRequested;

        Closing += OnWindowClosing;
        ContentRendered += OnContentRendered;
    }

    private void OnCloseRequested()
    {
        Close();
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        if (viewModel.CanCloseWindow())
            return;

        e.Cancel = true;
    }

    private async void OnContentRendered(object? sender, EventArgs e)
    {
        if (_startupPrepared)
            return;

        _startupPrepared = true;
        await WarmStartupSurfaceAsync();
        StartupShield.Visibility = Visibility.Collapsed;
    }

    private async Task WarmStartupSurfaceAsync()
    {
        int originalIndex = MainTabs.SelectedIndex;
        MainTabs.IsEnabled = false;

        try
        {
            for (int index = 0; index < MainTabs.Items.Count; index++)
            {
                MainTabs.SelectedIndex = index;
                await WaitForRenderPassAsync();
            }

            MainTabs.SelectedIndex = Math.Max(0, originalIndex);
            await WaitForRenderPassAsync();
        }
        finally
        {
            MainTabs.IsEnabled = true;
        }
    }

    private async Task WaitForRenderPassAsync()
    {
        UpdateLayout();
        InvalidateVisual();
        await Dispatcher.InvokeAsync(UpdateLayout, DispatcherPriority.Render);
        await Dispatcher.InvokeAsync(UpdateLayout, DispatcherPriority.ContextIdle);
    }
}
