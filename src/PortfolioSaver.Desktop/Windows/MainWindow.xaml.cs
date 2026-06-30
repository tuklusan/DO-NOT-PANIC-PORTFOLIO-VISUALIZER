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
using System.Windows.Input;
using System.Windows.Automation;
using System.Windows.Threading;
using PortfolioSaver.Shared;
using PortfolioSaver.Shared.Diagnostics;
using PortfolioSaver.Screensaver.Services;
using SettingsWindow = PortfolioSaver.Config.Windows.MainWindow;

namespace PortfolioSaver.Desktop.Windows;

public partial class MainWindow : Window
{
    private const double RestoredWindowWidth = 1180d;
    private const double RestoredWindowHeight = 720d;
    private bool _isFullScreen;
    private bool _suppressWindowConstraint;
    private WindowState _previousWindowState;
    private WindowStyle _previousWindowStyle;
    private ResizeMode _previousResizeMode;
    private bool _previousTopmost;
    private double _previousLeft;
    private double _previousTop;
    private double _previousWidth;
    private double _previousHeight;

    public MainWindow()
    {
        InitializeComponent();
        AutomationProperties.SetAutomationId(this, "DesktopMainWindow");
        AutomationProperties.SetName(this, PortfolioVersion.DisplayName);
        AutomationProperties.SetHelpText(this, PortfolioVersion.SemanticVersion);
        if (FullScreenMenuItem is not null)
        {
            AutomationProperties.SetName(FullScreenMenuItem, "Full Screen");
            AutomationProperties.SetHelpText(FullScreenMenuItem, "Enter or exit fullscreen mode");
        }
        if (OptionsMenuItem is not null)
        {
            AutomationProperties.SetName(OptionsMenuItem, "Options");
            AutomationProperties.SetHelpText(OptionsMenuItem, "Open desktop options");
        }
        if (SettingsMenuItem is not null)
        {
            AutomationProperties.SetName(SettingsMenuItem, "Settings");
            AutomationProperties.SetHelpText(SettingsMenuItem, "Open portfolio visualizer settings");
        }

        ApplyWindowStateConstraints();
    }

    public void ToggleFullScreen()
    {
        if (_isFullScreen)
        {
            ExitFullScreen();
        }
        else
        {
            EnterFullScreen();
        }
    }

    public void EnterFullScreen()
    {
        if (_isFullScreen)
        {
            return;
        }

        _previousWindowState = WindowState;
        _previousWindowStyle = WindowStyle;
        _previousResizeMode = ResizeMode;
        _previousTopmost = Topmost;
        _previousLeft = Left;
        _previousTop = Top;
        _previousWidth = Width;
        _previousHeight = Height;

        _suppressWindowConstraint = true;
        _isFullScreen = true;
        try
        {
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            MinWidth = 0d;
            MinHeight = 0d;
            MaxWidth = double.PositiveInfinity;
            MaxHeight = double.PositiveInfinity;
            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;
            Topmost = true;
        }
        finally
        {
            _suppressWindowConstraint = false;
        }

        // MainMenu is intentionally not data-bound; keep this manual state paired with ExitFullScreen.
        if (MainMenu is not null)
            MainMenu.Visibility = Visibility.Collapsed;

        Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(ApplyFullScreenBounds));
    }

    public void ExitFullScreen()
    {
        if (!_isFullScreen)
        {
            return;
        }

        Topmost = _previousTopmost;
        ResizeMode = _previousResizeMode;
        WindowStyle = _previousWindowStyle;
        if (MainMenu is not null)
            MainMenu.Visibility = Visibility.Visible;
        Left = _previousLeft;
        Top = _previousTop;
        WindowState = _previousWindowState;
        if (_previousWindowState == WindowState.Normal)
        {
            EnforceRestoredWindowSize();
        }
        else
        {
            Width = _previousWidth;
            Height = _previousHeight;
        }
        _isFullScreen = false;
        MinWidth = RestoredWindowWidth;
        MinHeight = RestoredWindowHeight;
        ApplyWindowStateConstraints();
    }

    private void OnExitClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnFullScreenClick(object sender, RoutedEventArgs e)
    {
        ToggleFullScreen();
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Topmost = true
        };
        void OnValidationActivityChanged(bool isValidating)
        {
            if (isValidating)
                SceneHost?.SetValidationPause(true);
        }

        window.ValidationActivityChanged += OnValidationActivityChanged;
        TraceLog.InfoState("Desktop.Config", "ConfigDialogOpening", []);
        SceneHost?.SetValidationPause(true);
        try
        {
            window.Loaded += (_, _) =>
            {
                window.Activate();
                window.Focus();
                TraceLog.InfoState("Desktop.Config", "ConfigDialogLoaded", []);
            };
            window.ShowDialog();
        }
        finally
        {
            window.ValidationActivityChanged -= OnValidationActivityChanged;
            SceneHost?.SetValidationPause(false);
            TraceLog.InfoState("Desktop.Config", "ConfigDialogClosed", []);
        }
    }

    private void OnAboutClick(object sender, RoutedEventArgs e)
    {
        AboutWindow window = new()
        {
            Owner = this,
            Topmost = Topmost
        };
        window.ShowDialog();
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11)
        {
            ToggleFullScreen();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && _isFullScreen)
        {
            ExitFullScreen();
            e.Handled = true;
        }
    }

    private void OnWindowStateChanged(object sender, EventArgs e)
    {
        if (_suppressWindowConstraint || _isFullScreen)
            return;

        ApplyWindowStateConstraints();
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_suppressWindowConstraint || _isFullScreen)
            return;

        if (WindowState == WindowState.Normal &&
            (Math.Abs(Width - RestoredWindowWidth) > 0.5d || Math.Abs(Height - RestoredWindowHeight) > 0.5d))
        {
            EnforceRestoredWindowSize();
        }
    }

    private void ApplyWindowStateConstraints()
    {
        if (_suppressWindowConstraint || _isFullScreen)
            return;

        if (WindowState == WindowState.Normal)
        {
            EnforceRestoredWindowSize();
        }
        else if (WindowState == WindowState.Maximized)
        {
            MaxWidth = SystemParameters.WorkArea.Width;
            MaxHeight = SystemParameters.WorkArea.Height;
        }
    }

    private void ApplyFullScreenBounds()
    {
        if (!_isFullScreen)
        {
            return;
        }

        _suppressWindowConstraint = true;
        try
        {
            MaxWidth = double.PositiveInfinity;
            MaxHeight = double.PositiveInfinity;
            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;
            Topmost = true;
        }
        finally
        {
            _suppressWindowConstraint = false;
        }
    }

    private void EnforceRestoredWindowSize()
    {
        _suppressWindowConstraint = true;
        try
        {
            Width = RestoredWindowWidth;
            Height = RestoredWindowHeight;
            MaxWidth = double.PositiveInfinity;
            MaxHeight = double.PositiveInfinity;
        }
        finally
        {
            _suppressWindowConstraint = false;
        }
    }
}
