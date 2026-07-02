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
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media;
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
    private DateTimeOffset _lastNativeLeftClickUtc = DateTimeOffset.MinValue;
    private Point _lastNativeLeftClickPoint;

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
            Rect bounds = GetCurrentMonitorBoundsInDips();
            Left = bounds.Left;
            Top = bounds.Top;
            Width = bounds.Width;
            Height = bounds.Height;
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
            new Action(ApplyFullScreenBoundsIfNeeded));
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

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        nint hwnd = new WindowInteropHelper(this).Handle;
        HwndSource? source = PresentationSource.FromVisual(this) as HwndSource
            ?? (hwnd != 0 ? HwndSource.FromHwnd(hwnd) : null);
        source?.AddHook(WndProc);
        TraceLog.InfoState(
            "Desktop.FullScreen",
            "HwndHookAttached",
            [new("hwnd", hwnd), new("attached", source is not null)]);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg is WmLeftButtonDown or WmNcLeftButtonDown)
        {
            Point point = GetClientPointFromLParam(lParam);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (ShouldToggleFullScreenFromNativeLeftButtonDown(
                    _lastNativeLeftClickUtc,
                    _lastNativeLeftClickPoint,
                    now,
                    point,
                    TimeSpan.FromMilliseconds(Math.Max(1, GetDoubleClickTime())),
                    Math.Max(1, GetSystemMetrics(SystemMetricCxDoubleClick)),
                    Math.Max(1, GetSystemMetrics(SystemMetricCyDoubleClick))))
            {
                TraceLog.InfoState(
                    "Desktop.FullScreen",
                    "NativeLeftButtonDoubleClickToggle",
                    [new("message", msg), new("x", point.X), new("y", point.Y), new("is_fullscreen_before", _isFullScreen)]);
                handled = true;
                _lastNativeLeftClickUtc = DateTimeOffset.MinValue;
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(ToggleFullScreen));
                return nint.Zero;
            }

            _lastNativeLeftClickUtc = now;
            _lastNativeLeftClickPoint = point;
        }

        if (ShouldToggleFullScreenFromNativeMessage(msg))
        {
            TraceLog.InfoState(
                "Desktop.FullScreen",
                "NativeDoubleClickToggle",
                [new("message", msg), new("is_fullscreen_before", _isFullScreen)]);
            handled = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(ToggleFullScreen));
        }

        return nint.Zero;
    }

    private void OnWindowPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        TraceLog.InfoState(
            "Desktop.FullScreen",
            "PreviewMouseLeftButtonDown",
            [new("click_count", e.ClickCount), new("is_menu_mouse_over", MainMenu?.IsMouseOver == true), new("source_type", e.OriginalSource?.GetType().FullName ?? "<null>")]);
        if (!ShouldToggleFullScreenFromLeftButtonDown(e.ClickCount, MainMenu?.IsMouseOver == true))
            return;

        if (ShouldSuppressDoubleClickFullScreenForInteractiveSource(e.OriginalSource as DependencyObject))
        {
            TraceLog.InfoState(
                "Desktop.FullScreen",
                "PreviewMouseLeftButtonDownSuppressed",
                [new("click_count", e.ClickCount), new("source_type", e.OriginalSource?.GetType().FullName ?? "<null>")]);
            return;
        }

        TraceLog.InfoState(
            "Desktop.FullScreen",
            "PreviewMouseLeftButtonDownToggle",
            [new("click_count", e.ClickCount), new("is_fullscreen_before", _isFullScreen)]);
        e.Handled = true;
        ToggleFullScreen();
    }

    private void OnWindowPreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (!ShouldToggleFullScreenFromDoubleClick(e.ChangedButton, MainMenu?.IsMouseOver == true))
            return;

        if (ShouldSuppressDoubleClickFullScreenForInteractiveSource(e.OriginalSource as DependencyObject))
            return;

        // Use the preview event so child scene controls cannot accidentally swallow the global shortcut.
        // Leave the routed event unhandled so future non-conflicting child double-click UX can still run.
        ToggleFullScreen();
    }

    internal static bool ShouldToggleFullScreenFromDoubleClick(MouseButton changedButton, bool isMenuMouseOver)
    {
        return changedButton == MouseButton.Left && !isMenuMouseOver;
    }

    internal static bool ShouldToggleFullScreenFromLeftButtonDown(int clickCount, bool isMenuMouseOver)
    {
        return clickCount >= 2 && !isMenuMouseOver;
    }

    internal static bool ShouldToggleFullScreenFromNativeMessage(int message)
    {
        return message is WmLeftButtonDoubleClick or WmNcLeftButtonDoubleClick;
    }

    internal static bool ShouldToggleFullScreenFromNativeLeftButtonDown(
        DateTimeOffset previousClickUtc,
        Point previousPoint,
        DateTimeOffset currentClickUtc,
        Point currentPoint,
        TimeSpan doubleClickTime,
        double doubleClickWidth,
        double doubleClickHeight)
    {
        if (previousClickUtc == DateTimeOffset.MinValue)
            return false;

        if (currentClickUtc < previousClickUtc || currentClickUtc - previousClickUtc > doubleClickTime)
            return false;

        return Math.Abs(currentPoint.X - previousPoint.X) <= doubleClickWidth &&
               Math.Abs(currentPoint.Y - previousPoint.Y) <= doubleClickHeight;
    }

    internal static bool ShouldSuppressDoubleClickFullScreenForInteractiveSource(DependencyObject? originalSource)
    {
        for (DependencyObject? current = originalSource; current is not null; current = GetParentObject(current))
        {
            if (current is MenuBase or MenuItem or ButtonBase or TextBoxBase or Selector or Slider or ScrollBar or Thumb or PasswordBox or TreeView or TreeViewItem)
                return true;
        }

        return false;
    }

    private static DependencyObject? GetParentObject(DependencyObject current)
    {
        if (current is FrameworkElement { Parent: DependencyObject parent })
            return parent;

        if (current is FrameworkContentElement { Parent: DependencyObject contentParent })
            return contentParent;

        try
        {
            return VisualTreeHelper.GetParent(current);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static Point GetClientPointFromLParam(nint lParam)
    {
        int value = lParam.ToInt32();
        short x = unchecked((short)(value & 0xFFFF));
        short y = unchecked((short)((value >> 16) & 0xFFFF));
        return new Point(x, y);
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
            // Let WPF own per-monitor maximized sizing; fixed SystemParameters.WorkArea caps
            // are primary-monitor DPI values and can crop or undersize large/wide displays.
            MaxWidth = double.PositiveInfinity;
            MaxHeight = double.PositiveInfinity;
        }
    }


    private void ApplyFullScreenBoundsIfNeeded()
    {
        if (!_isFullScreen)
            return;

        Rect bounds = GetCurrentMonitorBoundsInDips();
        bool alreadyAligned = Math.Abs(Left - bounds.Left) <= 0.5d &&
                              Math.Abs(Top - bounds.Top) <= 0.5d &&
                              Math.Abs(Width - bounds.Width) <= 0.5d &&
                              Math.Abs(Height - bounds.Height) <= 0.5d;
        if (alreadyAligned)
            return;

        _suppressWindowConstraint = true;
        try
        {
            Left = bounds.Left;
            Top = bounds.Top;
            Width = bounds.Width;
            Height = bounds.Height;
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

    private Rect GetCurrentMonitorBoundsInDips()
    {
        nint hwnd = new WindowInteropHelper(this).Handle;
        HwndSource? source = hwnd != 0 ? HwndSource.FromHwnd(hwnd) : null;
        Matrix transform = source?.CompositionTarget?.TransformFromDevice
            ?? PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice
            ?? Matrix.Identity;

        if (hwnd == 0 || source is null)
        {
            // Fullscreen is only entered after the WPF window is shown; this fallback keeps
            // unusual early calls safe without mixing raw pixels into WPF layout.
            return new Rect(0d, 0d, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
        }

        // Match normal Windows fullscreen behavior: occupy the monitor that owns this window.
        // Virtual-screen bounds caused mixed-DPI and ultrawide maximized/fullscreen artifacts.
        nint monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        MonitorInfo monitorInfo = new() { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == 0 || !GetMonitorInfo(monitor, ref monitorInfo))
            return new Rect(0d, 0d, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);

        Point topLeft = transform.Transform(new Point(monitorInfo.Monitor.Left, monitorInfo.Monitor.Top));
        Point bottomRight = transform.Transform(new Point(monitorInfo.Monitor.Right, monitorInfo.Monitor.Bottom));

        double width = Math.Max(1d, bottomRight.X - topLeft.X);
        double height = Math.Max(1d, bottomRight.Y - topLeft.Y);
        return new Rect(topLeft.X, topLeft.Y, width, height);
    }

    // Win32 MONITOR_DEFAULTTONEAREST: use the nearest monitor if the window straddles displays.
    private const uint MonitorDefaultToNearest = 2;
    private const int SystemMetricCxDoubleClick = 36;
    private const int SystemMetricCyDoubleClick = 37;
    private const int WmNcLeftButtonDown = 0x00A1;
    private const int WmNcLeftButtonDoubleClick = 0x00A3;
    private const int WmLeftButtonDown = 0x0201;
    private const int WmLeftButtonDoubleClick = 0x0203;

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo monitorInfo);

    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeRect
    {
        public readonly int Left;
        public readonly int Top;
        public readonly int Right;
        public readonly int Bottom;
    }
}
