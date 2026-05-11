using System.Windows;
using System.Windows.Input;
using System.Windows.Automation;
using PortfolioSaver.Shared;
using SettingsWindow = PortfolioSaver.Config.Windows.MainWindow;

namespace PortfolioSaver.Desktop.Windows;

public partial class MainWindow : Window
{
    private bool _isFullScreen;
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

        WindowState = WindowState.Normal;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        Topmost = true;
        _isFullScreen = true;
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
        Left = _previousLeft;
        Top = _previousTop;
        Width = _previousWidth;
        Height = _previousHeight;
        WindowState = _previousWindowState;
        _isFullScreen = false;
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
            Owner = this
        };
        window.ShowDialog();
    }

    private void OnAboutClick(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            $"{PortfolioVersion.DisplayName}{Environment.NewLine}{Environment.NewLine}" +
            $"Publisher: {AppIdentity.PublisherName}{Environment.NewLine}" +
            $"Version: {PortfolioVersion.SemanticVersion}{Environment.NewLine}" +
            "License: MIT LICENSE",
            PortfolioVersion.ProductName,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
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
}
