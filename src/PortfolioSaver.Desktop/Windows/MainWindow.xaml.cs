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
using SettingsWindow = PortfolioSaver.Config.Windows.MainWindow;

namespace PortfolioSaver.Desktop.Windows;

public partial class MainWindow : Window
{
    private const double RestoredWindowWidth = 1180d;
    private const double RestoredWindowHeight = 720d;
    private static readonly TimeSpan CompositionSurfaceNudgeInterval = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan CompositionSurfaceNudgeMinimumSpacing = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan CompositionSurfaceNudgeTransitionDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan FullScreenBoundsRepairDebounce = TimeSpan.FromMilliseconds(250);
    private static readonly bool DisableFullScreenInputExit =
        string.Equals(
            Environment.GetEnvironmentVariable("PORTFOLIOSAVER_DISABLE_INPUT_EXIT"),
            "1",
            StringComparison.OrdinalIgnoreCase);
    private readonly DispatcherTimer _compositionSurfaceNudgeTimer = new(DispatcherPriority.Background);
    private readonly DispatcherTimer _compositionSurfaceDelayedNudgeTimer = new(DispatcherPriority.Background);
    private readonly DispatcherTimer _fullScreenBoundsRepairTimer = new(DispatcherPriority.Background);
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
    private DateTimeOffset _lastCompositionSurfaceNudgeUtc = DateTimeOffset.MinValue;
    private int _compositionSurfaceNudgeCount;
    private string _pendingFullScreenBoundsRepairReason = "unspecified";
    private string _pendingCompositionSurfaceNudgeReason = "unspecified";
    private bool _pendingCompositionSurfaceNudgeRequiresFullScreen;
    private bool _pendingCompositionSurfaceNudgeForce;

    internal bool CompositionSurfaceNudgeTimerEnabled => _compositionSurfaceNudgeTimer.IsEnabled;
    internal TimeSpan CompositionSurfaceNudgeTimerInterval => _compositionSurfaceNudgeTimer.Interval;
    internal bool CompositionSurfaceDelayedNudgeTimerEnabled => _compositionSurfaceDelayedNudgeTimer.IsEnabled;
    internal TimeSpan CompositionSurfaceDelayedNudgeTimerInterval => _compositionSurfaceDelayedNudgeTimer.Interval;
    internal bool FullScreenBoundsRepairTimerEnabled => _fullScreenBoundsRepairTimer.IsEnabled;
    internal TimeSpan FullScreenBoundsRepairTimerInterval => _fullScreenBoundsRepairTimer.Interval;

    public MainWindow()
    {
        InitializeComponent();
        AutomationProperties.SetAutomationId(this, "DesktopMainWindow");
        AutomationProperties.SetName(this, PortfolioVersion.DisplayName);
        AutomationProperties.SetHelpText(this, PortfolioVersion.Version);
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
        ConfigureFullScreenBoundsRepairTimer();
        ConfigureCompositionSurfaceDelayedNudgeTimer();
        ConfigureCompositionSurfaceNudgeTimer();
    }

    public void ToggleFullScreen()
    {
        TraceFullScreenWindowState("ToggleRequested");
        if (_isFullScreen)
        {
            if (DisableFullScreenInputExit)
            {
                TraceFullScreenWindowState(
                    "ToggleIgnored",
                    TraceField("reason", "input_exit_disabled"));
                return;
            }

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

        TraceFullScreenWindowState(
            "FullScreenEnterStart",
            TraceField("previous_window_state", _previousWindowState),
            TraceField("previous_window_style", _previousWindowStyle),
            TraceField("previous_resize_mode", _previousResizeMode),
            TraceField("previous_topmost", _previousTopmost),
            TraceField("previous_left", Math.Round(_previousLeft, 1)),
            TraceField("previous_top", Math.Round(_previousTop, 1)),
            TraceField("previous_width", Math.Round(_previousWidth, 1)),
            TraceField("previous_height", Math.Round(_previousHeight, 1)));

        Rect bounds = Rect.Empty;
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
            bounds = GetCurrentMonitorBoundsInDips();
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

        TraceFullScreenWindowState(
            "FullScreenEnterApplied",
            TraceField("target_left", Math.Round(bounds.Left, 1)),
            TraceField("target_top", Math.Round(bounds.Top, 1)),
            TraceField("target_width", Math.Round(bounds.Width, 1)),
            TraceField("target_height", Math.Round(bounds.Height, 1)));

        Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(ApplyFullScreenBoundsIfNeeded));
        ScheduleCompositionSurfaceNudge(
            "fullscreen-enter",
            CompositionSurfaceNudgeTransitionDelay,
            requireFullScreen: true,
            force: true);
    }

    public void ExitFullScreen()
    {
        if (!_isFullScreen)
        {
            return;
        }
        if (DisableFullScreenInputExit)
        {
            TraceFullScreenWindowState(
                "FullScreenExitIgnored",
                TraceField("reason", "input_exit_disabled"));
            return;
        }

        TraceFullScreenWindowState(
            "FullScreenExitStart",
            TraceField("restore_window_state", _previousWindowState),
            TraceField("restore_window_style", _previousWindowStyle),
            TraceField("restore_resize_mode", _previousResizeMode),
            TraceField("restore_topmost", _previousTopmost),
            TraceField("restore_left", Math.Round(_previousLeft, 1)),
            TraceField("restore_top", Math.Round(_previousTop, 1)),
            TraceField("restore_width", Math.Round(_previousWidth, 1)),
            TraceField("restore_height", Math.Round(_previousHeight, 1)));

        _suppressWindowConstraint = true;
        try
        {
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
                Width = RestoredWindowWidth;
                Height = RestoredWindowHeight;
                MaxWidth = double.PositiveInfinity;
                MaxHeight = double.PositiveInfinity;
            }
            else
            {
                Width = _previousWidth;
                Height = _previousHeight;
            }
            MinWidth = RestoredWindowWidth;
            MinHeight = RestoredWindowHeight;
            MaxWidth = double.PositiveInfinity;
            MaxHeight = double.PositiveInfinity;
        }
        finally
        {
            _suppressWindowConstraint = false;
        }

        _isFullScreen = false;
        ApplyWindowStateConstraints();
        TraceFullScreenWindowState("FullScreenExitApplied");
        ScheduleCompositionSurfaceNudge(
            "fullscreen-exit",
            CompositionSurfaceNudgeTransitionDelay,
            requireFullScreen: false,
            force: true);
    }

    private void TraceFullScreenWindowState(
        string eventName,
        params KeyValuePair<string, object?>[] fields)
    {
        List<KeyValuePair<string, object?>> combined = new(fields)
        {
            new("is_fullscreen", _isFullScreen),
            new("window_state", WindowState),
            new("window_style", WindowStyle),
            new("resize_mode", ResizeMode),
            new("topmost", Topmost),
            new("left", Math.Round(Left, 1)),
            new("top", Math.Round(Top, 1)),
            new("width", Math.Round(Width, 1)),
            new("height", Math.Round(Height, 1)),
            new("actual_width", Math.Round(ActualWidth, 1)),
            new("actual_height", Math.Round(ActualHeight, 1)),
            new("min_width", Math.Round(MinWidth, 1)),
            new("min_height", Math.Round(MinHeight, 1)),
            new("max_width", double.IsPositiveInfinity(MaxWidth) ? "Infinity" : Math.Round(MaxWidth, 1)),
            new("max_height", double.IsPositiveInfinity(MaxHeight) ? "Infinity" : Math.Round(MaxHeight, 1))
        };
        TraceLog.InfoState("Desktop.FullScreen", eventName, combined);
    }

    private static KeyValuePair<string, object?> TraceField(string name, object? value)
        => new(name, value);

    private static bool IsCompositionSurfaceNudgeDisabled()
        => string.Equals(
            Environment.GetEnvironmentVariable("PORTFOLIOSAVER_DISABLE_COMPOSITION_NUDGE"),
            "1",
            StringComparison.OrdinalIgnoreCase);

    internal static bool ShouldApplyNativeFullScreenBounds(bool isFullScreen, bool fullScreenBoundsNeedRepair, bool nativeBoundsAvailable)
        => isFullScreen && fullScreenBoundsNeedRepair && nativeBoundsAvailable;

    internal static int CalculateNativePixelTolerance(double scale)
    {
        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0d)
            scale = 1d;

        double rawTolerance = Math.Ceiling(2d * scale);
        if (double.IsNaN(rawTolerance) || double.IsInfinity(rawTolerance) || rawTolerance > int.MaxValue)
            rawTolerance = int.MaxValue;

        return Math.Max(2, (int)rawTolerance);
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

    protected override void OnClosed(EventArgs e)
    {
        _compositionSurfaceNudgeTimer.Stop();
        _compositionSurfaceNudgeTimer.Tick -= OnCompositionSurfaceNudgeTimerTick;
        _compositionSurfaceDelayedNudgeTimer.Stop();
        _compositionSurfaceDelayedNudgeTimer.Tick -= OnCompositionSurfaceDelayedNudgeTimerTick;
        _fullScreenBoundsRepairTimer.Stop();
        _fullScreenBoundsRepairTimer.Tick -= OnFullScreenBoundsRepairTimerTick;
        base.OnClosed(e);
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
        {
            TraceLog.InfoState(
                "Desktop.FullScreen",
                "PreviewMouseDoubleClickSuppressed",
                [new("changed_button", e.ChangedButton), new("source_type", e.OriginalSource?.GetType().FullName ?? "<null>")]);
            return;
        }

        TraceLog.InfoState(
            "Desktop.FullScreen",
            "PreviewMouseDoubleClickToggle",
            [new("changed_button", e.ChangedButton), new("is_fullscreen_before", _isFullScreen)]);
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
            TraceFullScreenWindowState(
                "KeyboardToggleRequested",
                TraceField("key", e.Key));
            ToggleFullScreen();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && _isFullScreen)
        {
            TraceFullScreenWindowState(
                "KeyboardExitRequested",
                TraceField("key", e.Key));
            ExitFullScreen();
            e.Handled = true;
        }
    }

    private void OnWindowStateChanged(object sender, EventArgs e)
    {
        TraceFullScreenWindowState(
            "WindowStateChanged",
            TraceField("suppress_window_constraint", _suppressWindowConstraint));

        if (_isFullScreen)
        {
            QueueFullScreenBoundsRepair("window-state-changed");
            return;
        }

        if (_suppressWindowConstraint)
            return;

        ApplyWindowStateConstraints();
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool sizeWillBeConstrained = WindowState == WindowState.Normal &&
                                     (Math.Abs(Width - RestoredWindowWidth) > 0.5d || Math.Abs(Height - RestoredWindowHeight) > 0.5d);
        if (_suppressWindowConstraint || _isFullScreen || sizeWillBeConstrained)
        {
            TraceFullScreenWindowState(
                "WindowSizeChanged",
                TraceField("suppress_window_constraint", _suppressWindowConstraint),
                TraceField("previous_width", Math.Round(e.PreviousSize.Width, 1)),
                TraceField("previous_height", Math.Round(e.PreviousSize.Height, 1)),
                TraceField("new_width", Math.Round(e.NewSize.Width, 1)),
                TraceField("new_height", Math.Round(e.NewSize.Height, 1)),
                TraceField("size_will_be_constrained", sizeWillBeConstrained));
            if (_isFullScreen && !_suppressWindowConstraint)
            {
                QueueFullScreenBoundsRepair("window-size-changed");
            }
        }

        if (_suppressWindowConstraint || _isFullScreen)
            return;

        if (sizeWillBeConstrained)
        {
            EnforceRestoredWindowSize();
        }
    }

    private void OnWindowLocationChanged(object? sender, EventArgs e)
    {
        if (!_isFullScreen || _suppressWindowConstraint || WindowState == WindowState.Minimized)
            return;

        TraceFullScreenWindowState(
            "WindowLocationChanged",
            TraceField("suppress_window_constraint", _suppressWindowConstraint));
        QueueFullScreenBoundsRepair("window-location-changed");
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
        {
            TraceFullScreenWindowState(
                "FullScreenBoundsRecheckAligned",
                TraceField("target_left", Math.Round(bounds.Left, 1)),
                TraceField("target_top", Math.Round(bounds.Top, 1)),
                TraceField("target_width", Math.Round(bounds.Width, 1)),
                TraceField("target_height", Math.Round(bounds.Height, 1)));
            return;
        }

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

        TraceFullScreenWindowState(
            "FullScreenBoundsReapplied",
            TraceField("target_left", Math.Round(bounds.Left, 1)),
            TraceField("target_top", Math.Round(bounds.Top, 1)),
            TraceField("target_width", Math.Round(bounds.Width, 1)),
            TraceField("target_height", Math.Round(bounds.Height, 1)));
    }

    private void QueueFullScreenBoundsRepair(string reason)
    {
        if (!_isFullScreen || _suppressWindowConstraint)
            return;

        bool wasAlreadyQueued = _fullScreenBoundsRepairTimer.IsEnabled;
        _pendingFullScreenBoundsRepairReason = reason;
        _fullScreenBoundsRepairTimer.Stop();
        _fullScreenBoundsRepairTimer.Start();
        TraceFullScreenWindowState(
            "FullScreenBoundsRepairQueued",
            TraceField("reason", reason),
            TraceField("debounce_milliseconds", FullScreenBoundsRepairDebounce.TotalMilliseconds),
            TraceField("was_already_queued", wasAlreadyQueued));
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

    private void ConfigureFullScreenBoundsRepairTimer()
    {
        _fullScreenBoundsRepairTimer.Interval = FullScreenBoundsRepairDebounce;
        _fullScreenBoundsRepairTimer.Tick += OnFullScreenBoundsRepairTimerTick;
    }

    private void ConfigureCompositionSurfaceDelayedNudgeTimer()
    {
        _compositionSurfaceDelayedNudgeTimer.Interval = CompositionSurfaceNudgeTransitionDelay;
        _compositionSurfaceDelayedNudgeTimer.Tick += OnCompositionSurfaceDelayedNudgeTimerTick;
    }

    private void OnFullScreenBoundsRepairTimerTick(object? sender, EventArgs e)
    {
        _fullScreenBoundsRepairTimer.Stop();
        string reason = _pendingFullScreenBoundsRepairReason;
        try
        {
            if (!_isFullScreen)
                return;

            ApplyFullScreenBoundsIfNeeded();
            RequestCompositionSurfaceNudge(
                $"fullscreen-bounds-repair-{reason}",
                requireFullScreen: true,
                force: true);
        }
        catch (Exception ex)
        {
            TraceLog.InfoState(
                "Desktop.Composition",
                "FullScreenBoundsRepairFailed",
                [
                    TraceField("reason", reason),
                    TraceField("exception", ex.ToString())
                ]);
        }
    }

    private void ConfigureCompositionSurfaceNudgeTimer()
    {
        if (IsCompositionSurfaceNudgeDisabled())
        {
            TraceLog.InfoState(
                "Desktop.Composition",
                "CompositionSurfaceNudgeTimerDisabled",
                [TraceField("reason", "environment")]);
            return;
        }

        _compositionSurfaceNudgeTimer.Interval = CompositionSurfaceNudgeInterval;
        _compositionSurfaceNudgeTimer.Tick += OnCompositionSurfaceNudgeTimerTick;
        _compositionSurfaceNudgeTimer.Start();
        TraceLog.InfoState(
            "Desktop.Composition",
            "CompositionSurfaceNudgeTimerStarted",
            [TraceField("interval_seconds", CompositionSurfaceNudgeInterval.TotalSeconds)]);
    }

    private void OnCompositionSurfaceNudgeTimerTick(object? sender, EventArgs e)
    {
        CheckFullScreenCompositionSurface("periodic-fullscreen-composition-watchdog");
    }

    private void CheckFullScreenCompositionSurface(string reason)
    {
        if (IsCompositionSurfaceNudgeDisabled() || !_isFullScreen || !IsLoaded || WindowState == WindowState.Minimized)
            return;

        bool needsRepair = IsFullScreenBoundsRepairNeeded(
            out Rect targetDipBounds,
            out bool wpfBoundsAligned,
            out bool nativeBoundsAvailable,
            out bool nativeBoundsAligned,
            out NativeRect nativeWindowRect,
            out NativeRect nativeTargetRect,
            out int nativePixelTolerance);
        if (needsRepair)
        {
            QueueFullScreenBoundsRepair(reason);
            return;
        }

        TraceLog.InfoState(
            "Desktop.Composition",
            "CompositionSurfaceBoundsRepairSkipped",
            [
                TraceField("reason", reason),
                TraceField("wpf_bounds_aligned", wpfBoundsAligned),
                TraceField("native_bounds_available", nativeBoundsAvailable),
                TraceField("native_bounds_aligned", nativeBoundsAligned),
                TraceField("native_pixel_tolerance", nativePixelTolerance),
                TraceField("target_left", Math.Round(targetDipBounds.Left, 1)),
                TraceField("target_top", Math.Round(targetDipBounds.Top, 1)),
                TraceField("target_width", Math.Round(targetDipBounds.Width, 1)),
                TraceField("target_height", Math.Round(targetDipBounds.Height, 1)),
                TraceField("native_window_rect", nativeBoundsAvailable ? FormatNativeRect(nativeWindowRect) : null),
                TraceField("native_fullscreen_target", nativeBoundsAvailable ? FormatNativeRect(nativeTargetRect) : null)
            ]);
        RequestCompositionSurfaceNudge(reason, requireFullScreen: true, force: false);
    }

    private void ScheduleCompositionSurfaceNudge(string reason, TimeSpan delay, bool requireFullScreen, bool force)
    {
        if (IsCompositionSurfaceNudgeDisabled())
            return;

        _pendingCompositionSurfaceNudgeReason = reason;
        _pendingCompositionSurfaceNudgeRequiresFullScreen = requireFullScreen;
        _pendingCompositionSurfaceNudgeForce = force;
        _compositionSurfaceDelayedNudgeTimer.Stop();
        _compositionSurfaceDelayedNudgeTimer.Interval = delay;
        _compositionSurfaceDelayedNudgeTimer.Start();
    }

    private void OnCompositionSurfaceDelayedNudgeTimerTick(object? sender, EventArgs e)
    {
        _compositionSurfaceDelayedNudgeTimer.Stop();
        RequestCompositionSurfaceNudge(
            _pendingCompositionSurfaceNudgeReason,
            _pendingCompositionSurfaceNudgeRequiresFullScreen,
            _pendingCompositionSurfaceNudgeForce);
    }

    private void RequestCompositionSurfaceNudge(string reason, bool requireFullScreen, bool force)
    {
        if (IsCompositionSurfaceNudgeDisabled())
            return;
        if (requireFullScreen && !_isFullScreen)
            return;
        if (!IsLoaded || WindowState == WindowState.Minimized)
            return;

        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (!force && _lastCompositionSurfaceNudgeUtc != DateTimeOffset.MinValue &&
            now - _lastCompositionSurfaceNudgeUtc < CompositionSurfaceNudgeMinimumSpacing)
        {
            return;
        }

        nint hwnd = new WindowInteropHelper(this).Handle;
        _compositionSurfaceNudgeCount++;
        int nudgeCount = _compositionSurfaceNudgeCount;
        TimeSpan sincePrevious = _lastCompositionSurfaceNudgeUtc == DateTimeOffset.MinValue
            ? TimeSpan.Zero
            : now - _lastCompositionSurfaceNudgeUtc;
        _lastCompositionSurfaceNudgeUtc = now;

        bool setWindowPosOk = false;
        int setWindowPosError = 0;
        bool setWindowPosSkipped = false;
        bool fullScreenBoundsNeedRepair = false;
        bool wpfBoundsAligned = true;
        bool nativeFullScreenBoundsApplied = false;
        bool nativeBoundsAvailable = false;
        bool nativeBoundsAligned = true;
        int nativePixelTolerance = GetNativePixelTolerance();
        bool getWindowRectOk = false;
        int getWindowRectError = 0;
        NativeRect windowRectBefore = default;
        NativeRect nativeFullScreenTarget = default;
        bool redrawWindowOk = false;
        int redrawWindowError = 0;
        try
        {
            // WPF invalidation refreshes the scene tree; Win32 redraw pokes the presented HWND.
            // Native reposition is reserved for proven fullscreen-bounds drift to avoid flicker.
            SceneHost?.RequestHostCompositionNudge(reason);
            InvalidateVisual();

            if (hwnd != 0)
            {
                if (_isFullScreen)
                {
                    fullScreenBoundsNeedRepair = IsFullScreenBoundsRepairNeeded(
                        out _,
                        out wpfBoundsAligned,
                        out nativeBoundsAvailable,
                        out nativeBoundsAligned,
                        out windowRectBefore,
                        out nativeFullScreenTarget,
                        out nativePixelTolerance);
                    getWindowRectOk = nativeBoundsAvailable;
                }
                else
                {
                    getWindowRectOk = GetWindowRect(hwnd, out windowRectBefore);
                    if (!getWindowRectOk)
                        getWindowRectError = Marshal.GetLastPInvokeError();
                }

                if (ShouldApplyNativeFullScreenBounds(_isFullScreen, fullScreenBoundsNeedRepair, nativeBoundsAvailable))
                {
                    uint setWindowPosFlags = SwpNoActivate | SwpNoOwnerZOrder | SwpNoZOrder | SwpFrameChanged;
                    nativeFullScreenBoundsApplied = true;
                    int targetX = nativeFullScreenTarget.Left;
                    int targetY = nativeFullScreenTarget.Top;
                    int targetWidth = Math.Max(1, nativeFullScreenTarget.Right - nativeFullScreenTarget.Left);
                    int targetHeight = Math.Max(1, nativeFullScreenTarget.Bottom - nativeFullScreenTarget.Top);
                    setWindowPosOk = SetWindowPos(
                        hwnd,
                        HwndTop,
                        targetX,
                        targetY,
                        targetWidth,
                        targetHeight,
                        setWindowPosFlags);
                    if (!setWindowPosOk)
                        setWindowPosError = Marshal.GetLastPInvokeError();
                }
                else
                {
                    setWindowPosSkipped = true;
                }

                redrawWindowOk = RedrawWindow(
                    hwnd,
                    0,
                    0,
                    RdwInvalidate | RdwFrame | RdwAllChildren);
                if (!redrawWindowOk)
                    redrawWindowError = Marshal.GetLastPInvokeError();
            }

            TraceLog.InfoState(
                "Desktop.Composition",
                "CompositionSurfaceNudge",
                [
                    TraceField("reason", reason),
                    TraceField("count", nudgeCount),
                    TraceField("hwnd", hwnd),
                    TraceField("is_fullscreen", _isFullScreen),
                    TraceField("window_state", WindowState),
                    TraceField("fullscreen_bounds_need_repair", fullScreenBoundsNeedRepair),
                    TraceField("wpf_bounds_aligned", wpfBoundsAligned),
                    TraceField("native_bounds_available", nativeBoundsAvailable),
                    TraceField("native_bounds_aligned", nativeBoundsAligned),
                    TraceField("native_pixel_tolerance", nativePixelTolerance),
                    TraceField("native_fullscreen_bounds_applied", nativeFullScreenBoundsApplied),
                    TraceField("get_window_rect_ok", getWindowRectOk),
                    TraceField("get_window_rect_error", getWindowRectError),
                    TraceField("window_rect_before", getWindowRectOk ? FormatNativeRect(windowRectBefore) : null),
                    TraceField("native_fullscreen_target", nativeBoundsAvailable ? FormatNativeRect(nativeFullScreenTarget) : null),
                    TraceField("set_window_pos_skipped", setWindowPosSkipped),
                    TraceField("set_window_pos_ok", setWindowPosOk),
                    TraceField("set_window_pos_error", setWindowPosError),
                    TraceField("redraw_window_ok", redrawWindowOk),
                    TraceField("redraw_window_error", redrawWindowError),
                    TraceField("seconds_since_previous", _compositionSurfaceNudgeCount == 1 ? null : Math.Round(sincePrevious.TotalSeconds, 1))
                ]);
        }
        catch (Exception ex)
        {
            TraceLog.InfoState(
                "Desktop.Composition",
                "CompositionSurfaceNudgeFailed",
                [
                    TraceField("reason", reason),
                    TraceField("count", nudgeCount),
                    TraceField("hwnd", hwnd),
                    TraceField("exception", ex.ToString())
                ]);
        }
    }

    private static bool TryGetCurrentMonitorRect(nint hwnd, out NativeRect monitorRect)
    {
        monitorRect = default;
        if (hwnd == 0)
            return false;

        nint monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        MonitorInfo monitorInfo = new() { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == 0 || !GetMonitorInfo(monitor, ref monitorInfo))
            return false;

        monitorRect = monitorInfo.Monitor;
        return true;
    }

    private bool IsFullScreenBoundsRepairNeeded(
        out Rect targetDipBounds,
        out bool wpfBoundsAligned,
        out bool nativeBoundsAvailable,
        out bool nativeBoundsAligned,
        out NativeRect nativeWindowRect,
        out NativeRect nativeTargetRect,
        out int nativePixelTolerance)
    {
        nativePixelTolerance = GetNativePixelTolerance();
        targetDipBounds = GetCurrentMonitorBoundsInDips();
        wpfBoundsAligned = Math.Abs(Left - targetDipBounds.Left) <= 0.5d &&
                           Math.Abs(Top - targetDipBounds.Top) <= 0.5d &&
                           Math.Abs(Width - targetDipBounds.Width) <= 0.5d &&
                           Math.Abs(Height - targetDipBounds.Height) <= 0.5d;

        nativeBoundsAvailable = false;
        nativeBoundsAligned = true;
        nativeWindowRect = default;
        nativeTargetRect = default;

        nint hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != 0 &&
            GetWindowRect(hwnd, out nativeWindowRect) &&
            TryGetCurrentMonitorRect(hwnd, out nativeTargetRect))
        {
            nativeBoundsAvailable = true;
            nativeBoundsAligned =
                Math.Abs(nativeWindowRect.Left - nativeTargetRect.Left) <= nativePixelTolerance &&
                Math.Abs(nativeWindowRect.Top - nativeTargetRect.Top) <= nativePixelTolerance &&
                Math.Abs((nativeWindowRect.Right - nativeWindowRect.Left) - (nativeTargetRect.Right - nativeTargetRect.Left)) <= nativePixelTolerance &&
                Math.Abs((nativeWindowRect.Bottom - nativeWindowRect.Top) - (nativeTargetRect.Bottom - nativeTargetRect.Top)) <= nativePixelTolerance;
        }

        return !wpfBoundsAligned || (nativeBoundsAvailable && !nativeBoundsAligned);
    }

    private int GetNativePixelTolerance()
    {
        HwndSource? source = PresentationSource.FromVisual(this) as HwndSource;
        Matrix transform = source?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        double scale = Math.Max(Math.Abs(transform.M11), Math.Abs(transform.M22));
        return CalculateNativePixelTolerance(scale);
    }

    private static string FormatNativeRect(NativeRect rect)
        => $"{rect.Left},{rect.Top},{rect.Right - rect.Left},{rect.Bottom - rect.Top}";

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
    private static readonly nint HwndTop = 0;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private const uint RdwInvalidate = 0x0001;
    private const uint RdwAllChildren = 0x0080;
    private const uint RdwFrame = 0x0400;

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo monitorInfo);

    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RedrawWindow(nint hWnd, nint lprcUpdate, nint hrgnUpdate, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hWnd, out NativeRect lpRect);

    // These structs must remain blittable/sequential for the Win32 P/Invoke calls above.
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

