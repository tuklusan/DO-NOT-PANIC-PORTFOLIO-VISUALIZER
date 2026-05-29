using System.Windows;
using System.Windows.Automation;
using System.Windows.Threading;
using System.ComponentModel;
using PortfolioSaver.Config.ViewModels;
using PortfolioSaver.Shared;

namespace PortfolioSaver.Config.Windows;

public partial class MainWindow : Window
{
    private bool _startupPrepared;
    private ValidationProgressWindow? _validationProgressWindow;

    public event Action<bool>? ValidationActivityChanged;

    public MainWindow()
    {
        InitializeComponent();
        AutomationProperties.SetAutomationId(this, "ConfigMainWindow");
        AutomationProperties.SetName(this, Title);
        AutomationProperties.SetHelpText(this, PortfolioVersion.SemanticVersion);

        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.CloseRequested += OnCloseRequested;
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        Closing += OnWindowClosing;
        ContentRendered += OnContentRendered;
        Closed += OnWindowClosed;
    }

    private void OnCloseRequested()
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(OnCloseRequested, DispatcherPriority.Send);
            return;
        }

        if (IsVisible)
            Hide();

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

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MainWindowViewModel viewModel)
            return;

        if (!string.Equals(e.PropertyName, nameof(MainWindowViewModel.IsApplying), StringComparison.Ordinal))
            return;

        ToggleValidationProgressWindow(viewModel);
        ValidationActivityChanged?.Invoke(viewModel.IsApplying);
    }

    private void ToggleValidationProgressWindow(MainWindowViewModel viewModel)
    {
        if (viewModel.IsApplying)
        {
            if (_validationProgressWindow is null)
            {
                _validationProgressWindow = new ValidationProgressWindow
                {
                    Owner = this,
                    DataContext = viewModel
                };
                _validationProgressWindow.Closed += OnValidationProgressWindowClosed;
            }

            if (!_validationProgressWindow.IsVisible)
                _validationProgressWindow.Show();

            return;
        }

        CloseValidationProgressWindow();
    }

    private void OnValidationProgressWindowClosed(object? sender, EventArgs e)
    {
        if (_validationProgressWindow is null)
            return;

        _validationProgressWindow.Closed -= OnValidationProgressWindowClosed;
        _validationProgressWindow = null;
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.CloseRequested -= OnCloseRequested;
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        CloseValidationProgressWindow();
    }

    private void CloseValidationProgressWindow()
    {
        if (_validationProgressWindow is null)
            return;

        _validationProgressWindow.Close();
    }
}
