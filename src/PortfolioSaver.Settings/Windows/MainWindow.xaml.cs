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
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
        AutomationProperties.SetHelpText(this, PortfolioVersion.Version);

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

    private void OnHelpBadgeClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.ToolTip is null)
            return;

        bool createdTooltip = element.ToolTip is not System.Windows.Controls.ToolTip;
        System.Windows.Controls.ToolTip tooltip = element.ToolTip as System.Windows.Controls.ToolTip ?? new System.Windows.Controls.ToolTip
        {
            Content = new TextBlock
            {
                Text = element.ToolTip.ToString() ?? string.Empty,
                MaxWidth = 420,
                TextWrapping = TextWrapping.Wrap
            },
            StaysOpen = false
        };
        tooltip.PlacementTarget = element;
        tooltip.Placement = PlacementMode.Bottom;
        if (createdTooltip)
            element.ToolTip = tooltip;

        if (!tooltip.IsOpen)
            tooltip.IsOpen = true;
        e.Handled = true;
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
