using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Automation;
using PortfolioSaver.Core.Enums;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Core.Services;
using PortfolioSaver.Render.Controls;
using PortfolioSaver.Render.Services;
using PortfolioSaver.Render.ViewModels;
using PortfolioSaver.Screensaver.Services;
using PortfolioSaver.Shared.Diagnostics;
using PortfolioSaver.Shared.Helpers;
using PortfolioSaver.Shared;

namespace PortfolioSaver.Screensaver.Controls;

public partial class ScreensaverSceneControl : UserControl
{
    private static readonly bool EnableMarketCritters = false;
    private const string GlobalMarketsWaitingGlyph = "🕒";
    private const int MaxVisibleGraphCards = 12;
    private readonly ObservableCollection<FloatingGraphViewModel> _graphs = [];
    private readonly ObservableCollection<MarketSpriteViewModel> _marketSprites = [];
    private readonly ObservableCollection<TapeViewModel> _tapes = [];
    private readonly StartupCoordinator _startupCoordinator = new();
    private readonly FloatingSpriteMotionController _motionController = new();
    private readonly NewYorkMarketStatusService _marketStatusService = new();
    private readonly ExchangeMarketCalendarService _exchangeMarketCalendarService = new();
    private readonly DispatcherTimer _clockTimer = new();
    private readonly DispatcherTimer _refreshTimer = new();
    private readonly DispatcherTimer _backgroundTimer = new();
    private readonly DispatcherTimer _backgroundZoomTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly DispatcherTimer _worldDataTimer = new();
    private readonly DispatcherTimer _demoFlashTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly DispatcherTimer _motionTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Random _random = new();
    private readonly WorldWeatherService _worldWeatherService = new();
    private readonly NtpTimeService _ntpTimeService = new();
    private readonly NetworkAvailabilityService _networkAvailabilityService = new();
    private Image? _activeBackgroundImage;
    private Image? _inactiveBackgroundImage;

    private AppSettings _settings = new();
    private FloatingClockViewModel? _clockViewModel;
    private NetworkWaitingViewModel? _networkWaitingViewModel;
    private StatusBarViewModel? _statusViewModel;
    private readonly NewsFlasherViewModel _newsViewModel = new();
    private List<string> _backgroundPaths = [];
    private IReadOnlyDictionary<string, QuoteSnapshot> _latestQuotes = new Dictionary<string, QuoteSnapshot>(StringComparer.OrdinalIgnoreCase);
    private string? _currentBackgroundPath;
    private DateTime _lastMotionTick = DateTime.UtcNow;
    private bool _initialized;
    private bool _isRefreshing;
    private bool _hasSeededLayout;
    private int _graphRotationSeed;
    private CancellationTokenSource? _graphWarmupCancellation;
    private Task? _graphWarmupTask;
    private CancellationTokenSource? _captureSequenceCancellation;
    private CancellationTokenSource? _startupWarmupCancellation;
    private TimeSpan? _ntpOffset;
    private DateTimeOffset _lastNtpSyncUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _lastWeatherRefreshUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _lastMarketCalendarRefreshUtc = DateTimeOffset.MinValue;
    private IReadOnlyDictionary<string, WeatherSnapshot> _weatherSnapshots = new Dictionary<string, WeatherSnapshot>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<decimal>> _clockIndexHistory = new(StringComparer.OrdinalIgnoreCase);
    private ExchangeCalendarSet _exchangeCalendars = new();
    private double _backgroundZoomScale = 1.01d;
    private double _backgroundZoomDirection = 1d;
    private int _demoFlashTicks;
    private DateTimeOffset _lastCritterTargetSwapUtc = DateTimeOffset.MinValue;
    private bool _crittersChasingDollar = true;
    private DateTimeOffset _lastMacroMeterRefreshUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _lastMacroTraceUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _lastClockMarketTraceUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _lastStatusAncillaryRefreshUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _lastClockAncillaryRefreshUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _lastSceneHeartbeatUtc = DateTimeOffset.MinValue;

    public ScreensaverSceneControl()
    {
        InitializeComponent();
        if (VersionWatermark is not null)
        {
            VersionWatermark.Text = PortfolioVersion.SemanticVersion;
            AutomationProperties.SetAutomationId(VersionWatermark, "ScreensaverVersionWatermark");
            AutomationProperties.SetName(VersionWatermark, $"Version {PortfolioVersion.SemanticVersion}");
            AutomationProperties.SetHelpText(VersionWatermark, PortfolioVersion.SemanticVersion);
        }
        _marketStatusService.UpdateCalendarSnapshot(_exchangeMarketCalendarService.LoadNyseSnapshotFromCacheOrOffline());
        TapeItemsControl.ItemsSource = _tapes;
        NewsFlasherHost.Content = _newsViewModel;
        if (EnableMarketCritters)
        {
            MarketSpriteItemsControl.ItemsSource = _marketSprites;
            MarketSpriteItemsControl.Visibility = Visibility.Visible;
        }
        else
        {
            MarketSpriteItemsControl.Visibility = Visibility.Collapsed;
        }

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
        _clockTimer.Tick += (_, _) => UpdateClocks();
        _refreshTimer.Tick += async (_, _) => await RefreshSceneAsync(preserveLayout: true);
        _backgroundTimer.Tick += (_, _) => RotateBackground();
        _backgroundZoomTimer.Tick += (_, _) => StepBackgroundSlowZoom();
        _worldDataTimer.Tick += async (_, _) => await RefreshClockDataAsync(force: true);
        _demoFlashTimer.Tick += (_, _) => RunDemoFlashPulse();
        _motionTimer.Tick += (_, _) => StepMotion();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
            return;

        try
        {
            TraceScene("OnLoaded starting.");
            _initialized = true;
            _activeBackgroundImage = BackgroundImageA;
            _inactiveBackgroundImage = BackgroundImageB;
            ApplySceneState(_startupCoordinator.BuildBootstrapScene(), preserveLayout: false);
            _refreshTimer.Stop();
            RestartGraphWarmup(_graphRotationSeed, preserveLayout: false);
            _ = RefreshClockDataAsync(force: true);
            _startupWarmupCancellation = new CancellationTokenSource();
            await RunStartupWarmupAsync(_startupWarmupCancellation.Token);
            await RefreshSceneAsync(preserveLayout: false);
            StartCaptureSequenceIfRequested();
            StartDemoFlashSequence();
            TraceScene("OnLoaded completed.");
        }
        catch (Exception ex)
        {
            TraceScene($"OnLoaded failed: {ex}");
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _clockTimer.Stop();
        _refreshTimer.Stop();
        _backgroundTimer.Stop();
        _backgroundZoomTimer.Stop();
        _worldDataTimer.Stop();
        StopDemoFlashSequence();
        _motionTimer.Stop();
        CancelGraphWarmup();
        CancelCaptureSequence();
        CancelStartupWarmup();
        _initialized = false;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyResponsiveLayout();
        if (!_hasSeededLayout && _graphs.Count > 0)
            SeedSpriteLayout(onlyMissingPositions: false);
    }

    private async Task RefreshSceneAsync(bool preserveLayout)
    {
        if (_isRefreshing)
            return;

        _isRefreshing = true;
        try
        {
            int currentRotationSeed = _graphRotationSeed;
            ScreensaverSceneState state = await _startupCoordinator.BuildSceneAsync(currentRotationSeed);
            ApplySceneState(state, preserveLayout);
            RestartGraphWarmup(currentRotationSeed, preserveLayout);
            if (preserveLayout && _settings.EnableFloatingGraphs)
                _graphRotationSeed++;
        }
        catch (Exception ex)
        {
            TraceScene($"RefreshSceneAsync failed: {ex}");
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private void ApplySceneState(ScreensaverSceneState state, bool preserveLayout)
    {
        TraceScene($"ApplySceneState preserveLayout={preserveLayout} tapes={state.Tapes.Count} graphs={state.Graphs.Count} backgrounds={state.BackgroundPaths.Count} news={state.News.Headlines.Count} waiting={state.ShowNetworkWaitingOverlay}");
        _settings = state.Settings;
        _latestQuotes = MergeQuotes(_latestQuotes, state.Quotes);
        _statusViewModel = state.Status;
        CompactStatusText();
        StatusBarHost.DataContext = _statusViewModel;
        UpdateStatusMacroMeters(force: true);
        SyncTapes(_startupCoordinator.BuildTapesForQuotes(_settings, _latestQuotes));
        SyncNews(state.News);
        ApplyDimOpacity(state.Settings.DimOpacity);
        _backgroundPaths = state.BackgroundPaths
            .Where(IsSupportedBackgroundReference)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (string.IsNullOrWhiteSpace(_currentBackgroundPath) || !_backgroundPaths.Contains(_currentBackgroundPath, StringComparer.OrdinalIgnoreCase))
            _currentBackgroundPath = null;

        if (_currentBackgroundPath is null)
            RotateBackground(forceDifferent: false);

        ApplyNetworkWaitingOverlay(state);

        if (state.Graphs.Count > 0 || (!preserveLayout && _graphs.Count == 0))
        {
            Dictionary<string, FloatingGraphViewModel> previousGraphs = preserveLayout
                ? _graphs.ToDictionary(GetGraphKey, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, FloatingGraphViewModel>(StringComparer.OrdinalIgnoreCase);

            _graphs.Clear();
            foreach (FloatingGraphViewModel graph in state.Graphs)
            {
                if (previousGraphs.TryGetValue(GetGraphKey(graph), out FloatingGraphViewModel? previous))
                {
                    CopyMotion(previous, graph);
                }

                _graphs.Add(graph);
            }
        }

        SyncGraphVisuals();
        ApplyQuotesToGraphs();

        if (state.Clock is null)
        {
            GlobalMarketsTapeHost.Visibility = Visibility.Collapsed;
            GlobalMarketsTapeHost.Content = null;
            GlobalMarketsTapeHost.DataContext = null;
            _clockViewModel = null;
        }
        else
        {
            _clockViewModel = state.Clock;
            GlobalMarketsTapeHost.Visibility = Visibility.Visible;
            GlobalMarketsTapeHost.DataContext = _clockViewModel;
            GlobalMarketsTapeHost.Content = _clockViewModel;
        }

        UpdateLayout();
        _hasSeededLayout = preserveLayout && _graphs.All(graph => graph.X > 0 || graph.Y > 0);
        ApplyResponsiveLayout();
        SeedSpriteLayout(onlyMissingPositions: preserveLayout);
        UpdateClocks(forceAncillaryRefresh: true);
        ApplyWeatherToClock();
        ApplyClockMarketData(force: true);
        ConfigureTimers();
        _ = RefreshClockDataAsync(force: false);
        TraceDisplayedTapeSample();
        TraceSceneStateSummary("ApplySceneStateComplete", preserveLayout);
    }

    private void RestartGraphWarmup(int rotationSeed, bool preserveLayout)
    {
        if (!_settings.EnableFloatingGraphs)
        {
            CancelGraphWarmup();
            _graphs.Clear();
            SyncGraphVisuals();
            return;
        }

        if (preserveLayout && _graphWarmupTask is not null && !_graphWarmupTask.IsCompleted)
        {
            TraceScene("RestartGraphWarmup skipped because a graph warmup is already running.");
            return;
        }

        CancelGraphWarmup();

        CancellationTokenSource cancellation = new();
        _graphWarmupCancellation = cancellation;
        _graphWarmupTask = WarmGraphsAsync(rotationSeed, preserveLayout, cancellation.Token);
    }

    private void CancelGraphWarmup()
    {
        if (_graphWarmupCancellation is null)
            return;

        try
        {
            _graphWarmupCancellation.Cancel();
        }
        catch
        {
        }

        _graphWarmupCancellation.Dispose();
        _graphWarmupCancellation = null;
    }

    private void CancelStartupWarmup()
    {
        if (_startupWarmupCancellation is null)
            return;

        try
        {
            _startupWarmupCancellation.Cancel();
        }
        catch
        {
        }

        _startupWarmupCancellation.Dispose();
        _startupWarmupCancellation = null;
    }

    private async Task RunStartupWarmupAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (StartupWarmupBatch batch in _startupCoordinator.WarmStartupYahooQuotesAsync(_settings, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                _latestQuotes = MergeQuotes(_latestQuotes, batch.Quotes);
                SyncTapes(_startupCoordinator.BuildTapesForQuotes(_settings, _latestQuotes));
                ApplyQuotesToGraphs();

                if (_statusViewModel is not null)
                {
                    _statusViewModel.ProviderText = "Provider: Yahoo Finance warmup";
                    UpdateStatusFreshnessText(batch.StatusMessage);
                    DateTimeOffset referenceUtc = GetReferenceUtcNow();
                    _statusViewModel.ClockDateText = FormatStatusClockDate(referenceUtc);
                    _statusViewModel.ClockText = FormatClockTimeWithZone(referenceUtc, TimeZoneInfo.Utc);
                    UpdateStatusMacroMeters(force: true);
                }

                ApplyClockMarketData(force: true);

                TraceSceneState(
                    "WarmupBatchApplied",
                    new KeyValuePair<string, object?>("completed_batches", batch.CompletedBatches),
                    new KeyValuePair<string, object?>("total_batches", batch.TotalBatches),
                    new KeyValuePair<string, object?>("quote_count", _latestQuotes.Count),
                    new KeyValuePair<string, object?>("provider_text", _statusViewModel?.ProviderText),
                    new KeyValuePair<string, object?>("updated_text", _statusViewModel?.UpdatedText));

            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            TraceScene($"RunStartupWarmupAsync failed: {ex}");
        }
        finally
        {
            CancelStartupWarmup();
        }
    }

    private void StartCaptureSequenceIfRequested()
    {
        CancelCaptureSequence();

        string captureDirectory = (Environment.GetEnvironmentVariable("PORTFOLIOSAVER_CAPTURE_DIR") ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(captureDirectory))
            return;

        string captureStem = (Environment.GetEnvironmentVariable("PORTFOLIOSAVER_CAPTURE_STEM") ?? "screensaver-capture").Trim();
        List<int> captureScheduleSeconds = ParseCaptureSchedule(
            Environment.GetEnvironmentVariable("PORTFOLIOSAVER_CAPTURE_DELAYS"));
        if (captureScheduleSeconds.Count == 0)
            return;

        CancellationTokenSource cancellation = new();
        _captureSequenceCancellation = cancellation;
        _ = CaptureSceneSequenceAsync(captureDirectory, captureStem, captureScheduleSeconds, cancellation.Token);
    }

    private void CancelCaptureSequence()
    {
        if (_captureSequenceCancellation is null)
            return;

        try
        {
            _captureSequenceCancellation.Cancel();
        }
        catch
        {
        }

        _captureSequenceCancellation.Dispose();
        _captureSequenceCancellation = null;
    }

    private async Task CaptureSceneSequenceAsync(
        string captureDirectory,
        string captureStem,
        IReadOnlyList<int> captureScheduleSeconds,
        CancellationToken cancellationToken)
    {
        try
        {
            DateTimeOffset startedAt = DateTimeOffset.UtcNow;
            for (int index = 0; index < captureScheduleSeconds.Count; index++)
            {
                int targetSeconds = Math.Max(1, captureScheduleSeconds[index]);
                TimeSpan delay = startedAt.AddSeconds(targetSeconds) - DateTimeOffset.UtcNow;
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();
                string targetPath = Path.Combine(captureDirectory, $"{captureStem}-{index + 1}.png");
                await Dispatcher.InvokeAsync(() => SaveSceneCapture(targetPath), DispatcherPriority.Render, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void SaveSceneCapture(string targetPath)
    {
        UpdateLayout();
        int pixelWidth = Math.Max(1, (int)Math.Round(ActualWidth));
        int pixelHeight = Math.Max(1, (int)Math.Round(ActualHeight));
        if (pixelWidth <= 1 || pixelHeight <= 1)
            return;

        TraceScene($"Saving capture {targetPath} at {pixelWidth}x{pixelHeight}.");

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

        RenderTargetBitmap bitmap = new(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(this);

        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream stream = File.Create(targetPath);
        encoder.Save(stream);
    }

    private static List<int> ParseCaptureSchedule(string? rawSchedule)
    {
        if (string.IsNullOrWhiteSpace(rawSchedule))
            return [8];

        return rawSchedule
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => int.TryParse(value, out int seconds) ? seconds : 0)
            .Where(seconds => seconds > 0)
            .Distinct()
            .OrderBy(seconds => seconds)
            .ToList();
    }

    private static void TraceScene(string message)
    {
        TraceLog.Info("Screensaver.Scene", message);
    }

    private static void TraceSceneState(string eventName, params KeyValuePair<string, object?>[] fields)
    {
        TraceLog.InfoState("Screensaver.Scene", eventName, fields);
    }

    private void TraceSceneStateSummary(string eventName, bool preserveLayout)
    {
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        if (nowUtc - _lastSceneHeartbeatUtc < TimeSpan.FromSeconds(20))
            return;

        _lastSceneHeartbeatUtc = nowUtc;
        int staleQuoteCount = _latestQuotes.Values.Count(IsQuoteBeyondStaleThreshold);
        TraceSceneState(
            eventName,
            new KeyValuePair<string, object?>("preserve_layout", preserveLayout),
            new KeyValuePair<string, object?>("quote_count", _latestQuotes.Count),
            new KeyValuePair<string, object?>("stale_quote_count", staleQuoteCount),
            new KeyValuePair<string, object?>("graph_count", _graphs.Count),
            new KeyValuePair<string, object?>("tape_count", _tapes.Count),
            new KeyValuePair<string, object?>("provider_text", _statusViewModel?.ProviderText),
            new KeyValuePair<string, object?>("updated_text", _statusViewModel?.UpdatedText),
            new KeyValuePair<string, object?>("waiting_overlay_visible", NetworkWaitingHost.Visibility == Visibility.Visible),
            new KeyValuePair<string, object?>("clock_visible", _clockViewModel is not null));
    }

    private void TraceMacroSnapshot(bool force)
    {
        if (_statusViewModel is null)
            return;

        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        if (!force && nowUtc - _lastMacroTraceUtc < TimeSpan.FromSeconds(30))
            return;

        _lastMacroTraceUtc = nowUtc;
        List<string> macroTexts = _statusViewModel.MacroMeters
            .Select(meter => $"{meter.Label}:{meter.ValueText}:{meter.ChangeText}")
            .ToList();
        List<string> missingSymbols = StartupCoordinator.GetMacroIndicatorSymbols()
            .Where(symbol => !_latestQuotes.TryGetValue(symbol, out QuoteSnapshot? quote) || (quote.Last is null && quote.PreviousClose is null))
            .ToList();
        List<string> staleSymbols = StartupCoordinator.GetMacroIndicatorSymbols()
            .Where(symbol => _latestQuotes.TryGetValue(symbol, out QuoteSnapshot? quote) && IsQuoteBeyondStaleThreshold(quote))
            .ToList();

        TraceSceneState(
            "MacroSnapshot",
            new KeyValuePair<string, object?>("force", force),
            new KeyValuePair<string, object?>("meters", macroTexts),
            new KeyValuePair<string, object?>("missing_symbols", missingSymbols),
            new KeyValuePair<string, object?>("stale_symbols", staleSymbols));
    }

    private async Task WarmGraphsAsync(int rotationSeed, bool preserveLayout, CancellationToken cancellationToken)
    {
        try
        {
            TraceScene($"WarmGraphsAsync starting rotationSeed={rotationSeed} preserveLayout={preserveLayout}.");
            await foreach (FloatingGraphViewModel graph in _startupCoordinator.LoadGraphsIncrementallyAsync(_settings, rotationSeed, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                TraceScene($"WarmGraphsAsync yielded {graph.Symbol} for {graph.TapeName}.");
                ApplyOrUpdateGraph(graph, preserveLayout);
            }

            TraceScene("WarmGraphsAsync completed.");
        }
        catch (OperationCanceledException)
        {
            TraceScene("WarmGraphsAsync cancelled.");
        }
        catch (Exception ex)
        {
            TraceScene($"WarmGraphsAsync failed: {ex}");
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
                _graphWarmupTask = null;
        }
    }

    private void ApplyOrUpdateGraph(FloatingGraphViewModel graph, bool preserveLayout)
    {
        UpdateLayout();
        ApplyResponsiveLayout();
        string graphKey = GetGraphKey(graph);
        int existingIndex = -1;
        FloatingGraphViewModel? previous = null;
        for (int i = 0; i < _graphs.Count; i++)
        {
            if (!string.Equals(GetGraphKey(_graphs[i]), graphKey, StringComparison.OrdinalIgnoreCase))
                continue;

            existingIndex = i;
            previous = _graphs[i];
            break;
        }

        if (previous is not null)
            CopyMotion(previous, graph);

        if (existingIndex >= 0)
            _graphs[existingIndex] = graph;
        else
            _graphs.Add(graph);

        ApplyQuoteToGraph(graph);
        SeedSpriteLayout(onlyMissingPositions: preserveLayout || existingIndex >= 0);
        ClampSpritesToSafeBounds();
        SyncGraphVisuals();
        TraceScene(
            $"ApplyOrUpdateGraph key={graphKey} count={_graphs.Count} x={graph.X:F1} y={graph.Y:F1} w={graph.Width:F1} h={graph.Height:F1} host={FloatingGraphCanvas.ActualWidth:F1}x{FloatingGraphCanvas.ActualHeight:F1}");
    }

    private void ConfigureTimers()
    {
        _clockTimer.Interval = TimeSpan.FromSeconds(Math.Max(1, _settings.ClockRefreshSeconds));
        _refreshTimer.Interval = TimeSpan.FromSeconds(GetRefreshSeconds());
        _backgroundTimer.Interval = TimeSpan.FromSeconds(Math.Max(5, _settings.BackgroundChangeSeconds));
        _worldDataTimer.Interval = TimeSpan.FromMinutes(10);
        _clockTimer.Start();
        _refreshTimer.Start();
        _worldDataTimer.Start();
        if (_backgroundPaths.Count > 1)
            _backgroundTimer.Start();
        else
            _backgroundTimer.Stop();
        _lastMotionTick = DateTime.UtcNow;
        _motionTimer.Start();
        TraceSceneState(
            "TimersConfigured",
            new KeyValuePair<string, object?>("clock_seconds", _clockTimer.Interval.TotalSeconds),
            new KeyValuePair<string, object?>("refresh_seconds", _refreshTimer.Interval.TotalSeconds),
            new KeyValuePair<string, object?>("background_seconds", _backgroundTimer.Interval.TotalSeconds),
            new KeyValuePair<string, object?>("world_data_minutes", _worldDataTimer.Interval.TotalMinutes),
            new KeyValuePair<string, object?>("background_rotation_enabled", _backgroundPaths.Count > 1),
            new KeyValuePair<string, object?>("pending_quote_recovery", HasPendingQuoteRecovery()));
    }

    private double GetRefreshSeconds()
    {
        bool noResolvedQuotes = _latestQuotes.Count == 0;
        bool providerUnavailable = _statusViewModel?.ProviderText?.Contains("Unavailable", StringComparison.OrdinalIgnoreCase) == true;
        bool waitingForNetwork = _statusViewModel?.ProviderText?.Contains("Waiting for network", StringComparison.OrdinalIgnoreCase) == true;
        if (noResolvedQuotes || providerUnavailable || waitingForNetwork || HasPendingQuoteRecovery())
            return QuoteRefreshPolicy.RecoveryRefreshSeconds;

        return QuoteRefreshPolicy.GetRefreshPollingInterval(_settings, GetReferenceUtcNow()).TotalSeconds;
    }

    private bool HasPendingQuoteRecovery()
    {
        HashSet<string> expectedSymbols = new(StringComparer.OrdinalIgnoreCase);
        foreach (TickerGroup group in _settings.Groups.Where(group => group.Enabled))
        {
            foreach (TickerItem ticker in group.Tickers.Where(ticker => ticker.Enabled && !string.IsNullOrWhiteSpace(ticker.Symbol)))
                expectedSymbols.Add(ticker.Symbol);
        }

        foreach (string symbol in FloatingClockBuilder.GetWorldIndexSymbols())
            expectedSymbols.Add(symbol);

        foreach (string symbol in StartupCoordinator.GetMacroIndicatorSymbols())
            expectedSymbols.Add(symbol);

        if (expectedSymbols.Count == 0)
            return false;

        DateTimeOffset nowUtc = GetReferenceUtcNow();
        foreach (string symbol in expectedSymbols)
        {
            if (!_latestQuotes.TryGetValue(symbol, out QuoteSnapshot? quote))
                return true;

            if (IsQuoteBeyondStaleThreshold(quote))
                return true;

            if (quote.Last is null && quote.PreviousClose is null)
                return true;
        }

        return false;
    }

    private void ApplyResponsiveLayout()
    {
        double viewportWidth = Math.Max(ActualWidth, FloatingOverlayCanvas.ActualWidth);
        double viewportHeight = Math.Max(ActualHeight, FloatingOverlayCanvas.ActualHeight);
        if (viewportWidth <= 0 || viewportHeight <= 0)
            return;

        double graphWidth = Math.Clamp(viewportWidth * 0.10d, 150d, 196d);
        double graphHeight = Math.Clamp(viewportHeight * 0.072d, 64d, 84d);
        foreach (FloatingGraphViewModel graph in _graphs)
        {
            graph.Width = graphWidth;
            graph.Height = graphHeight;
            graph.PlotWidth = Math.Max(106d, graphWidth - 62d);
            graph.PlotHeight = Math.Max(34d, graphHeight - 38d);
        }

        double statusHeight = Math.Max(72d, StatusBarHost.ActualHeight);
        double tapeTopMargin = Math.Clamp(statusHeight + 12d, 78d, 126d);
        TapeItemsControl.Margin = new Thickness(0, tapeTopMargin, 0, 0);

        Rect canvasBounds = GetFullCanvasBounds();
        if (canvasBounds != Rect.Empty)
        {
            foreach (FloatingSpriteViewModel sprite in EnumerateSprites())
                ClampSpriteToBounds(sprite, canvasBounds);
        }
    }

    private void SeedSpriteLayout(bool onlyMissingPositions)
    {
        Rect bounds = GetGraphMotionBounds();
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        List<Rect> occupied = [];
        if (onlyMissingPositions)
        {
            foreach (FloatingSpriteViewModel sprite in EnumerateSprites())
            {
                if (sprite.Width <= 0 || sprite.Height <= 0)
                    continue;

                if (sprite.X > 0 || sprite.Y > 0)
                    occupied.Add(GetSpriteRect(sprite));
            }
        }

        List<string> enabledTapeNames = _settings.Groups
            .Where(group => group.Enabled)
            .Select(group => group.Name)
            .ToList();
        if (enabledTapeNames.Count == 0)
            enabledTapeNames.AddRange(_graphs.Select(graph => graph.TapeName).Distinct(StringComparer.OrdinalIgnoreCase));

        Dictionary<string, int> tapeIndexes = enabledTapeNames
            .Select((name, index) => new { name, index })
            .ToDictionary(item => item.name, item => item.index, StringComparer.OrdinalIgnoreCase);

        List<IGrouping<string, FloatingGraphViewModel>> tapes = _graphs
            .GroupBy(graph => graph.TapeName)
            .OrderBy(group => tapeIndexes.TryGetValue(group.Key, out int index) ? index : int.MaxValue)
            .ToList();

        int laneCount = Math.Max(1, enabledTapeNames.Count);
        for (int tapeGroupIndex = 0; tapeGroupIndex < tapes.Count; tapeGroupIndex++)
        {
            IGrouping<string, FloatingGraphViewModel> tapeGroup = tapes[tapeGroupIndex];
            int tapeIndex = tapeIndexes.TryGetValue(tapeGroup.Key, out int configuredIndex)
                ? configuredIndex
                : tapeGroupIndex;
            double segmentTop = bounds.Top + tapeIndex * bounds.Height / laneCount;
            double segmentHeight = bounds.Height / laneCount;

            int graphIndex = 0;
            foreach (FloatingGraphViewModel graph in tapeGroup)
            {
                if (onlyMissingPositions && (graph.X > 0 || graph.Y > 0))
                {
                    graphIndex++;
                    continue;
                }

                double preferredX = bounds.Left + ((graphIndex + 1d) / (tapeGroup.Count() + 1d)) * Math.Max(0, bounds.Width - graph.Width);
                double preferredY = segmentTop + Math.Max(0, segmentHeight - graph.Height) / 2;
                Rect segmentBounds = new(
                    bounds.Left + 8,
                    segmentTop + 10,
                    Math.Max(20, bounds.Width - 16),
                    Math.Max(graph.Height + 10, segmentHeight - 20));

                PlaceSprite(graph, segmentBounds, preferredX, preferredY, occupied);
                graph.VelocityX = NextVelocity();
                graph.VelocityY = NextVelocity();
                graph.NominalVelocityX = graph.VelocityX;
                graph.NominalVelocityY = graph.VelocityY;
                graph.RefreshTravelTargetY = null;
                graph.BounceWithinViewport = _settings.EnableBouncingGraphCards;
                graphIndex++;
            }
        }

        int tapeLaneCount = Math.Max(1, Math.Max(tapes.Count, enabledTapeNames.Count));
        double laneHeight = bounds.Height / tapeLaneCount;

        if (_networkWaitingViewModel is not null && (!onlyMissingPositions || (_networkWaitingViewModel.X <= 0 && _networkWaitingViewModel.Y <= 0)))
        {
            Rect waitingBounds = GetWaitingBounds();
            double preferredX = waitingBounds.Left + Math.Max(0, (waitingBounds.Width - _networkWaitingViewModel.Width) / 2);
            double preferredY = waitingBounds.Top + Math.Max(12, (laneHeight - _networkWaitingViewModel.Height) / 2d);
            PlaceSprite(_networkWaitingViewModel, waitingBounds, preferredX, preferredY, occupied);
            _networkWaitingViewModel.VelocityX = -Math.Abs(NextVelocity() * 0.8d);
            _networkWaitingViewModel.VelocityY = Math.Abs(NextVelocity() * 0.8d);
            _networkWaitingViewModel.BounceWithinViewport = true;
        }

        ClampSpritesToSafeBounds();
        _hasSeededLayout = true;
    }

    private void StepMotion()
    {
        Rect bounds = GetGraphMotionBounds();
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        DateTime currentTick = DateTime.UtcNow;
        double elapsedSeconds = Math.Max(0.001, (currentTick - _lastMotionTick).TotalSeconds);
        _lastMotionTick = currentTick;

        foreach (FloatingGraphViewModel graph in EnumerateVisibleGraphCards())
        {
            ApplyGraphRefreshImpulse(graph, bounds);
            _motionController.Step(graph, bounds, elapsedSeconds);
            ResetGraphRefreshImpulseIfNeeded(graph, bounds);
        }

        if (EnableMarketCritters)
            if (EnableMarketCritters)
                StepMarketSpriteMotion(elapsedSeconds);

        if (_networkWaitingViewModel is not null)
            _motionController.Step(_networkWaitingViewModel, GetWaitingBounds(), elapsedSeconds);

        ClampSpritesToSafeBounds();
    }

    private IEnumerable<FloatingGraphViewModel> EnumerateVisibleGraphCards()
        => _graphs.Where(graph => graph.IsVisible).Take(MaxVisibleGraphCards);

    private Rect GetBaseMotionBounds()
    {
        double width = FloatingOverlayCanvas.ActualWidth;
        double height = FloatingOverlayCanvas.ActualHeight;
        if (width <= 0 || height <= 0)
            return Rect.Empty;

        return new Rect(12, 12, Math.Max(20, width - 24), Math.Max(20, height - 24));
    }

    private Rect GetGraphMotionBounds()
        => GetFullCanvasBounds();

    private Rect GetWaitingBounds()
    {
        Rect graphBounds = GetGraphMotionBounds();
        if (graphBounds == Rect.Empty)
            return Rect.Empty;

        return new Rect(graphBounds.Left, graphBounds.Top, graphBounds.Width, Math.Max(40d, graphBounds.Height));
    }

    private Rect GetFullCanvasBounds()
    {
        double width = FloatingOverlayCanvas.ActualWidth;
        double height = FloatingOverlayCanvas.ActualHeight;
        return width <= 0 || height <= 0 ? Rect.Empty : new Rect(0, 0, width, height);
    }

    private Rect GetMarketSpriteBounds()
    {
        Rect full = GetFullCanvasBounds();
        if (full == Rect.Empty)
            return Rect.Empty;

        double top = full.Top + (full.Height * 0.55d);
        double bottom = full.Bottom - 92d;
        double height = Math.Max(70d, bottom - top);
        return new Rect(full.Left + 18d, top, Math.Max(80d, full.Width - 36d), height);
    }

    private void EnsureMarketSpritesInitialized()
    {
        if (!EnableMarketCritters)
            return;

        if (_marketSprites.Count > 0)
            return;

        _marketSprites.Add(new MarketSpriteViewModel
        {
            Key = "bull",
            SpriteText = "🐂",
            Foreground = Brushes.Goldenrod,
            Width = 34,
            Height = 34,
            IsBag = false
        });
        _marketSprites.Add(new MarketSpriteViewModel
        {
            Key = "bear",
            SpriteText = "🐻",
            Foreground = Brushes.Peru,
            Width = 34,
            Height = 34,
            IsBag = false
        });
        _marketSprites.Add(new MarketSpriteViewModel
        {
            Key = "dollar-bag",
            SpriteText = "💵",
            Foreground = Brushes.Honeydew,
            Width = 26,
            Height = 26,
            IsBag = true
        });
        _marketSprites.Add(new MarketSpriteViewModel
        {
            Key = "euro-bag",
            SpriteText = "💶",
            Foreground = Brushes.LemonChiffon,
            Width = 26,
            Height = 26,
            IsBag = true
        });

        SeedMarketSprites();
    }

    private void SeedMarketSprites()
    {
        Rect bounds = GetMarketSpriteBounds();
        if (bounds == Rect.Empty || _marketSprites.Count < 4)
            return;

        double laneTop = bounds.Top + Math.Max(10d, bounds.Height * 0.15d);
        double laneBottom = bounds.Bottom - 40d;
        double[] xPositions =
        [
            bounds.Left + bounds.Width * 0.12d,
            bounds.Left + bounds.Width * 0.22d,
            bounds.Left + bounds.Width * 0.70d,
            bounds.Left + bounds.Width * 0.80d
        ];

        for (int index = 0; index < _marketSprites.Count; index++)
        {
            MarketSpriteViewModel sprite = _marketSprites[index];
            sprite.X = xPositions[index];
            sprite.BaseY = index < 2 ? laneTop : laneBottom;
            sprite.Y = sprite.BaseY;
            sprite.Phase = index * 1.7d;
            sprite.VelocityX = index switch
            {
                0 => 24d,
                1 => 20d,
                2 => -18d,
                _ => -22d
            };
            sprite.VelocityY = 0d;
            sprite.ScaleX = sprite.VelocityX >= 0 ? 1d : -1d;
        }
    }

    private void StepMarketSpriteMotion(double elapsedSeconds)
    {
        EnsureMarketSpritesInitialized();
        Rect bounds = GetMarketSpriteBounds();
        if (bounds == Rect.Empty)
            return;

        if (_lastCritterTargetSwapUtc == DateTimeOffset.MinValue)
            _lastCritterTargetSwapUtc = DateTimeOffset.UtcNow;

        if (DateTimeOffset.UtcNow - _lastCritterTargetSwapUtc >= TimeSpan.FromSeconds(9))
        {
            _crittersChasingDollar = !_crittersChasingDollar;
            _lastCritterTargetSwapUtc = DateTimeOffset.UtcNow;
        }

        MarketSpriteViewModel? bull = _marketSprites.FirstOrDefault(sprite => sprite.Key == "bull");
        MarketSpriteViewModel? bear = _marketSprites.FirstOrDefault(sprite => sprite.Key == "bear");
        MarketSpriteViewModel? dollarBag = _marketSprites.FirstOrDefault(sprite => sprite.Key == "dollar-bag");
        MarketSpriteViewModel? euroBag = _marketSprites.FirstOrDefault(sprite => sprite.Key == "euro-bag");
        if (bull is null || bear is null || dollarBag is null || euroBag is null)
            return;

        StepMarketBagSprite(dollarBag, bounds, elapsedSeconds, 1d);
        StepMarketBagSprite(euroBag, bounds, elapsedSeconds, -1d);

        StepCritterChase(bull, _crittersChasingDollar ? dollarBag : euroBag, bounds, elapsedSeconds, 72d);
        StepCritterChase(bear, _crittersChasingDollar ? euroBag : dollarBag, bounds, elapsedSeconds, 68d);
    }

    private static void StepMarketBagSprite(MarketSpriteViewModel bag, Rect bounds, double elapsedSeconds, double driftDirection)
    {
        bag.X += bag.VelocityX * elapsedSeconds;
        double minX = bounds.Left;
        double maxX = Math.Max(bounds.Left, bounds.Right - bag.Width);
        if (bag.X <= minX)
        {
            bag.X = minX;
            bag.VelocityX = Math.Abs(bag.VelocityX == 0d ? 18d : bag.VelocityX);
        }
        else if (bag.X >= maxX)
        {
            bag.X = maxX;
            bag.VelocityX = -Math.Abs(bag.VelocityX == 0d ? 18d : bag.VelocityX);
        }
        else if (bag.VelocityX == 0d)
        {
            bag.VelocityX = 18d * driftDirection;
        }

        double bob = Math.Sin((DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d) * 2.2d + bag.Phase) * 6d;
        bag.Y = Math.Clamp(bag.BaseY + bob, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - bag.Height));
        bag.ScaleX = bag.VelocityX >= 0 ? 1d : -1d;
    }

    private static void StepCritterChase(MarketSpriteViewModel critter, MarketSpriteViewModel target, Rect bounds, double elapsedSeconds, double speed)
    {
        double targetX = target.X + ((target.Width - critter.Width) / 2d);
        double targetY = target.Y + ((target.Height - critter.Height) / 2d);
        Vector vector = new(targetX - critter.X, targetY - critter.Y);
        if (vector.Length > 0.001d)
        {
            vector.Normalize();
            critter.VelocityX = vector.X * speed;
            critter.VelocityY = vector.Y * speed;
        }

        critter.X += critter.VelocityX * elapsedSeconds;
        critter.Y += critter.VelocityY * elapsedSeconds;
        critter.X = Math.Clamp(critter.X, bounds.Left, Math.Max(bounds.Left, bounds.Right - critter.Width));
        critter.Y = Math.Clamp(critter.Y, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - critter.Height));
        critter.ScaleX = critter.VelocityX >= 0 ? 1d : -1d;
    }

    private void UpdateClocks(bool forceAncillaryRefresh = false)
    {
        DateTimeOffset referenceUtc = GetReferenceUtcNow();
        bool refreshClockAncillary = forceAncillaryRefresh ||
                                     referenceUtc - _lastClockAncillaryRefreshUtc >= TimeSpan.FromMinutes(1);
        bool refreshStatusAncillary = forceAncillaryRefresh ||
                                      referenceUtc - _lastStatusAncillaryRefreshUtc >= TimeSpan.FromMinutes(1);

        if (_clockViewModel is not null)
        {
            UpdateClockEntries(referenceUtc, refreshClockAncillary);
            if (refreshClockAncillary)
                _lastClockAncillaryRefreshUtc = referenceUtc;
        }

        if (_statusViewModel is not null)
        {
            _statusViewModel.ClockDateText = FormatStatusClockDate(referenceUtc);
            _statusViewModel.ClockText = FormatClockTimeWithZone(referenceUtc, TimeZoneInfo.Utc);
            UpdateStatusFreshnessText(_statusViewModel.UpdatedText);
            if (refreshStatusAncillary)
            {
                _statusViewModel.MarketStatusText = FormatStatusBandText(_marketStatusService.FormatStatusLine(referenceUtc));
                UpdateStatusMacroMeters(force: true);
                _lastStatusAncillaryRefreshUtc = referenceUtc;
            }
        }
    }

    private void UpdateStatusMacroMeters(bool force)
    {
        const decimal treasuryYieldMeterMax = 6m;

        if (_statusViewModel is null)
            return;

        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        if (!force && nowUtc - _lastMacroMeterRefreshUtc < TimeSpan.FromSeconds(8))
            return;

        EnsureMacroMetersInitialized();
        bool loadingInitialValues = StartupCoordinator.ShouldShowInitialValueLoadingStatus(_latestQuotes, _settings, GetReferenceUtcNow());
        UpdateQuoteMeter(_statusViewModel.MacroMeters[0], "VIX", "^VIX", 60m, loadingInitialValues, invertRiskColors: true);
        UpdateQuoteMeter(_statusViewModel.MacroMeters[1], "GOLD", "GC=F", 4000m, loadingInitialValues);
        UpdateQuoteMeter(_statusViewModel.MacroMeters[2], "UST2M", "US2M", treasuryYieldMeterMax, loadingInitialValues);
        UpdateQuoteMeter(_statusViewModel.MacroMeters[3], "UST10Y", "US10Y", treasuryYieldMeterMax, loadingInitialValues);
        UpdateYieldSpreadMeter(_statusViewModel.MacroMeters[4], loadingInitialValues);
        UpdateQuoteMeter(_statusViewModel.MacroMeters[5], "DXY", "DX-Y.NYB", 120m, loadingInitialValues, invertRiskColors: true);
        UpdateQuoteMeter(_statusViewModel.MacroMeters[6], "CRUDE", "BZ=F", 160m, loadingInitialValues);

        _lastMacroMeterRefreshUtc = nowUtc;
        TraceMacroSnapshot(force);
    }

    private void EnsureMacroMetersInitialized()
    {
        if (_statusViewModel is null)
            return;

        string[] labels = ["VIX", "GOLD", "UST2M", "UST10Y", "YLD SPRD", "DXY", "CRUDE"];
        while (_statusViewModel.MacroMeters.Count > labels.Length)
            _statusViewModel.MacroMeters.RemoveAt(_statusViewModel.MacroMeters.Count - 1);

        for (int index = 0; index < labels.Length; index++)
        {
            if (index >= _statusViewModel.MacroMeters.Count)
            {
                _statusViewModel.MacroMeters.Add(new MacroMeterViewModel
                {
                    Label = labels[index],
                    AccentBrush = Brushes.SlateGray,
                    ValueText = "--",
                    ChangeText = index == 4 ? "pts" : string.Empty
                });
                continue;
            }

            _statusViewModel.MacroMeters[index].Label = labels[index];
        }
    }

    private void UpdateYieldSpreadMeter(MacroMeterViewModel meter, bool loadingInitialValues)
    {
        meter.Label = "YLD SPRD";
        if (loadingInitialValues)
        {
            ApplyWaitingMacroMeter(meter, keepPointsSuffix: true);
            return;
        }

        meter.AccentBrush = Brushes.SlateGray;
        meter.ValueText = "--";
        meter.ChangeText = "pts";

        if (!_latestQuotes.TryGetValue("US10Y", out QuoteSnapshot? tenYear) ||
            !_latestQuotes.TryGetValue("US2M", out QuoteSnapshot? twoMonth))
        {
            meter.SetFill(0d);
            return;
        }

        decimal? tenYearLast = tenYear.Last ?? tenYear.PreviousClose;
        decimal? twoMonthLast = twoMonth.Last ?? twoMonth.PreviousClose;
        if (tenYearLast is null || twoMonthLast is null)
        {
            meter.SetFill(0d);
            return;
        }

        decimal spread = tenYearLast.Value - twoMonthLast.Value;
        meter.ValueText = $"{spread:+0.00;-0.00;0.00}";
        meter.ChangeText = "pts";
        meter.AccentBrush = spread switch
        {
            > 0m => Brushes.LimeGreen,
            < 0m => Brushes.OrangeRed,
            _ => Brushes.Gainsboro
        };
        meter.SetFill((double)Math.Clamp((spread + 2m) / 4m, 0m, 1m));
    }

    private void UpdateQuoteMeter(
        MacroMeterViewModel meter,
        string label,
        string symbol,
        decimal maxValue,
        bool loadingInitialValues,
        bool invertRiskColors = false)
    {
        meter.Label = label;
        if (loadingInitialValues)
        {
            ApplyWaitingMacroMeter(meter);
            return;
        }

        meter.AccentBrush = Brushes.SlateGray;
        meter.ValueText = "--";
        meter.ChangeText = string.Empty;

        if (!_latestQuotes.TryGetValue(symbol, out QuoteSnapshot? quote))
        {
            meter.SetFill(0d);
            return;
        }

        decimal? last = quote.Last ?? quote.PreviousClose;
        decimal? changePercent = quote.ChangePercent;
        if (last is null)
        {
            meter.SetFill(0d);
            return;
        }

        meter.ValueText = last.Value.ToString("0.00");
        meter.ChangeText = changePercent is decimal percent
            ? $"{(percent >= 0 ? "+" : string.Empty)}{percent:0.0}%"
            : string.Empty;
        bool isStale = IsQuoteBeyondStaleThreshold(quote);
        Brush upBrush = invertRiskColors ? Brushes.OrangeRed : Brushes.LimeGreen;
        Brush downBrush = invertRiskColors ? Brushes.LimeGreen : Brushes.OrangeRed;
        meter.AccentBrush = isStale
            ? Brushes.Goldenrod
            : changePercent switch
        {
            > 0m => upBrush,
            < 0m => downBrush,
            _ => Brushes.Gainsboro
        };
        meter.SetFill((double)Math.Clamp(last.Value / Math.Max(1m, maxValue), 0m, 1m));
    }

    private static void ApplyWaitingMacroMeter(MacroMeterViewModel meter, bool keepPointsSuffix = false)
    {
        meter.AccentBrush = Brushes.Goldenrod;
        meter.ValueText = GlobalMarketsWaitingGlyph;
        meter.ChangeText = keepPointsSuffix ? "pts" : string.Empty;
        meter.SetFill(0d);
    }

    private async Task RefreshClockDataAsync(bool force)
    {
        if (_clockViewModel is null)
            return;

        DateTimeOffset utcNow = DateTimeOffset.UtcNow;
        bool networkAvailable = _networkAvailabilityService.IsNetworkAvailable();

        bool shouldRefreshNtp = force || utcNow - _lastNtpSyncUtc >= TimeSpan.FromMinutes(10);
        if (shouldRefreshNtp)
        {
            if (networkAvailable)
            {
                NtpSyncResult syncResult = await _ntpTimeService.TryGetUtcNowAsync();
                if (syncResult.Success)
                {
                    _ntpOffset = syncResult.UtcNow - DateTimeOffset.UtcNow;
                    _lastNtpSyncUtc = utcNow;
                    _clockViewModel.Subtitle = string.Empty;
                }
                else
                {
                    _ntpOffset = null;
                    _lastNtpSyncUtc = utcNow;
                    _clockViewModel.Subtitle = string.Empty;
                }
            }
            else
            {
                _ntpOffset = null;
                _clockViewModel.Subtitle = string.Empty;
            }
        }

        bool shouldRefreshWeather = force || utcNow - _lastWeatherRefreshUtc >= TimeSpan.FromMinutes(10);
        if (shouldRefreshWeather)
        {
            _weatherSnapshots = await _worldWeatherService.GetWeatherAsync(_clockViewModel.Cities, networkAvailable);
            _lastWeatherRefreshUtc = utcNow;
        }

        bool shouldRefreshCalendars = force || utcNow - _lastMarketCalendarRefreshUtc >= TimeSpan.FromHours(Math.Max(1, _settings.MarketCalendarRefreshHours));
        if (shouldRefreshCalendars)
        {
            IReadOnlyList<ExchangeCalendarRequest> requests = BuildCalendarRequests();
            _exchangeCalendars = await _exchangeMarketCalendarService.GetCalendarSetAsync(
                _settings,
                requests,
                networkAvailable);
            _marketStatusService.UpdateCalendarSnapshot(_exchangeCalendars.BuildNyseSnapshot());
            _lastMarketCalendarRefreshUtc = utcNow;
        }

        ApplyWeatherToClock();
        UpdateClocks(forceAncillaryRefresh: true);
        ApplyClockMarketData(force: true);
        TraceSceneState(
            "ClockDataRefresh",
            new KeyValuePair<string, object?>("force", force),
            new KeyValuePair<string, object?>("network_available", networkAvailable),
            new KeyValuePair<string, object?>("ntp_refreshed", shouldRefreshNtp),
            new KeyValuePair<string, object?>("weather_refreshed", shouldRefreshWeather),
            new KeyValuePair<string, object?>("weather_snapshot_count", _weatherSnapshots.Count),
            new KeyValuePair<string, object?>("calendar_refreshed", shouldRefreshCalendars),
            new KeyValuePair<string, object?>("clock_subtitle", _clockViewModel.Subtitle));
    }

    private void UpdateClockEntries(DateTimeOffset referenceUtc, bool refreshAncillary)
    {
        if (_clockViewModel is null)
            return;

        foreach (ClockCityViewModel city in _clockViewModel.Cities)
        {
            TimeZoneInfo zone = string.Equals(city.Key, "Local", StringComparison.OrdinalIgnoreCase)
                ? TimeZoneInfo.Local
                : ResolveTimeZone(city.PrimaryTimeZoneId, city.SecondaryTimeZoneId);

            DateTimeOffset cityTime = TimeZoneInfo.ConvertTime(referenceUtc, zone);
            city.TimeText = FormatClockTimeWithZone(cityTime, zone);
            ApplyExchangeCardMarketStatus(city, referenceUtc);
            if (refreshAncillary || string.IsNullOrWhiteSpace(city.ZoneText))
            {
                city.ZoneText = BuildClockFooterWithMarketStatus(city, zone, cityTime, referenceUtc);
                ApplyClockCardTheme(city, cityTime);
            }
        }
    }

    private void ApplyClockMarketData(bool force)
    {
        if (_clockViewModel is null)
            return;

        List<string> missingSymbols = [];
        int populatedCount = 0;
        bool loadingInitialValues = StartupCoordinator.ShouldShowInitialValueLoadingStatus(_latestQuotes, _settings, GetReferenceUtcNow());
        foreach (ClockCityViewModel city in _clockViewModel.Cities.Where(city => city.ShowExchangeDetails))
        {
            if (string.IsNullOrWhiteSpace(city.ExchangeSymbol))
                continue;

            if (!_latestQuotes.TryGetValue(city.ExchangeSymbol, out QuoteSnapshot? quote))
            {
                missingSymbols.Add(city.ExchangeSymbol);
                continue;
            }

            if (loadingInitialValues)
            {
                city.IndexValueText = GlobalMarketsWaitingGlyph;
                city.IndexChangeText = "--";
                city.IndexChangeForeground = Brushes.Goldenrod;
                city.MiniGraphStroke = Brushes.Goldenrod;
                city.MiniGraphPoints = [];
                continue;
            }

            decimal? last = quote.Last ?? quote.PreviousClose;
            decimal? changePercent = quote.ChangePercent;
            Brush changeBrush = changePercent switch
            {
                > 0m => Brushes.LimeGreen,
                < 0m => Brushes.OrangeRed,
                _ => Brushes.Gainsboro
            };

            city.IndexValueText = last is decimal value
                ? value.ToString("0.00")
                : "--";
            city.IndexChangeText = changePercent is decimal percent
                ? $"{(percent >= 0 ? "+" : string.Empty)}{percent:0.00}%"
                : "--";
            city.IndexChangeForeground = changeBrush;
            city.MiniGraphStroke = changeBrush;
            if (last is decimal)
                populatedCount++;

            if (last is decimal lastValue)
            {
                if (!_clockIndexHistory.TryGetValue(city.ExchangeSymbol, out List<decimal>? history))
                {
                    history = [];
                    _clockIndexHistory[city.ExchangeSymbol] = history;
                }

                bool shouldAppend = history.Count == 0 || Math.Abs(history[^1] - lastValue) > 0.0001m;
                if (shouldAppend)
                {
                    history.Add(lastValue);
                    while (history.Count > 24)
                        history.RemoveAt(0);
                }

                if (force || shouldAppend || city.MiniGraphPoints.Count == 0)
                    city.MiniGraphPoints = BuildMiniGraphPoints(history, 72d, 12d);
            }
        }

        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        if (nowUtc - _lastClockMarketTraceUtc >= TimeSpan.FromSeconds(30))
        {
            _lastClockMarketTraceUtc = nowUtc;
            TraceSceneState(
                "ClockMarketDataSummary",
                new KeyValuePair<string, object?>("populated_exchange_count", populatedCount),
                new KeyValuePair<string, object?>("missing_exchange_count", missingSymbols.Count),
                new KeyValuePair<string, object?>("missing_exchange_symbols", missingSymbols.Take(10).ToList()));
        }
    }

    private static PointCollection BuildMiniGraphPoints(IReadOnlyList<decimal> values, double width, double height)
    {
        if (values.Count == 0)
            return [];

        if (values.Count == 1)
            return [new Point(0, height / 2d), new Point(width, height / 2d)];

        decimal min = values.Min();
        decimal max = values.Max();
        decimal range = max - min;
        if (range <= 0)
            range = 1;

        double stepX = width / (values.Count - 1);
        PointCollection points = [];
        for (int index = 0; index < values.Count; index++)
        {
            decimal normalized = (values[index] - min) / range;
            double y = height - (double)normalized * height;
            points.Add(new Point(index * stepX, y));
        }

        return points;
    }

    private void ApplyWeatherToClock()
    {
        if (_clockViewModel is null)
            return;

        foreach (ClockCityViewModel city in _clockViewModel.Cities)
        {
            if (!city.SupportsWeather)
            {
                city.WeatherGlyph = string.Empty;
                city.WeatherText = "Clock only";
                continue;
            }

            if (_weatherSnapshots.TryGetValue(city.Key, out WeatherSnapshot? snapshot))
            {
                city.WeatherGlyph = WorldWeatherService.GetGlyph(snapshot.WeatherCode, snapshot.IsDay);
                city.WeatherText = $"{Math.Round(snapshot.TemperatureCelsius):0}C";
            }
            else
            {
                city.WeatherGlyph = string.Empty;
                city.WeatherText = "Weather unavailable";
            }
        }
    }

    private DateTimeOffset GetReferenceUtcNow()
    {
        if (_ntpOffset.HasValue && DateTimeOffset.UtcNow - _lastNtpSyncUtc <= TimeSpan.FromMinutes(20))
            return DateTimeOffset.UtcNow + _ntpOffset.Value;

        return DateTimeOffset.UtcNow;
    }

    private static TimeZoneInfo ResolveTimeZone(string primaryId, string secondaryId)
    {
        foreach (string candidate in new[] { primaryId, secondaryId })
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(candidate);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.Local;
    }

    private static string GetZoneLabel(TimeZoneInfo zone, DateTimeOffset pointInTime)
        => zone.IsDaylightSavingTime(pointInTime.DateTime) ? zone.DaylightName : zone.StandardName;

    private static string GetTimeZoneAbbreviation(TimeZoneInfo zone, DateTimeOffset pointInTime)
    {
        string label = GetZoneLabel(zone, pointInTime);
        string[] parts = label.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string abbreviation = string.Concat(parts.Select(part => char.ToUpperInvariant(part[0])));
        return string.IsNullOrWhiteSpace(abbreviation) ? label : abbreviation;
    }

    private static string FormatClockTimeWithZone(DateTimeOffset pointInTime, TimeZoneInfo zone)
        => $"{pointInTime:HH:mm} {GetTimeZoneAbbreviation(zone, pointInTime)}";

    private static string FormatStatusClockDate(DateTimeOffset pointInTime)
        => pointInTime.ToString("ddd dd-MMM", CultureInfo.InvariantCulture).ToUpperInvariant();

    private static string FormatStatusBandText(string statusLine)
        => string.IsNullOrWhiteSpace(statusLine)
            ? "Market (New York): --"
            : statusLine.Replace(" | ", Environment.NewLine, StringComparison.Ordinal);

    private static string FormatExchangeCardStatusText(ExchangeCalendarStatus status)
        => status.IsOpen ? "OPEN" : "CLOSED";

    private static string GetClockFooter(ClockCityViewModel city, TimeZoneInfo zone, DateTimeOffset pointInTime)
        => GetTimeZoneAbbreviation(zone, pointInTime);

    private string BuildClockFooterWithMarketStatus(
        ClockCityViewModel city,
        TimeZoneInfo zone,
        DateTimeOffset cityTime,
        DateTimeOffset referenceUtc)
    {
        string zoneFooter = GetClockFooter(city, zone, cityTime);
        if (!city.ShowExchangeDetails)
            return zoneFooter;

        ExchangeTradingCalendar? calendar = _exchangeCalendars.TryGetByCityKey(city.Key);
        if (calendar is null)
            return zoneFooter;

        ExchangeCalendarStatus status = _exchangeMarketCalendarService.ResolveStatus(calendar, referenceUtc);
        string statusText = _exchangeMarketCalendarService.FormatCompactStatus(status);
        if (string.IsNullOrWhiteSpace(zoneFooter))
            return statusText;

        return $"{zoneFooter} | {statusText}";
    }

    private void ApplyExchangeCardMarketStatus(ClockCityViewModel city, DateTimeOffset referenceUtc)
    {
        if (!city.ShowExchangeDetails)
        {
            city.MarketStatusText = string.Empty;
            city.MarketStatusForeground = Brushes.Gainsboro;
            return;
        }

        ExchangeTradingCalendar? calendar = _exchangeCalendars.TryGetByCityKey(city.Key);
        if (calendar is null)
        {
            city.MarketStatusText = "--";
            city.MarketStatusForeground = Brushes.Gainsboro;
            return;
        }

        ExchangeCalendarStatus status = _exchangeMarketCalendarService.ResolveStatus(calendar, referenceUtc);
        city.MarketStatusText = FormatExchangeCardStatusText(status);
        city.MarketStatusForeground = status.IsOpen ? Brushes.LimeGreen : Brushes.OrangeRed;
    }

    private IReadOnlyList<ExchangeCalendarRequest> BuildCalendarRequests()
    {
        if (_clockViewModel is null)
            return [];

        return _clockViewModel.Cities
            .Where(city => city.ShowExchangeDetails && !string.IsNullOrWhiteSpace(city.CalendarExchangeCode))
            .Select(city => new ExchangeCalendarRequest
            {
                CityKey = city.Key,
                ExchangeCode = city.CalendarExchangeCode,
                ExchangeName = city.ExchangeName,
                TimeZoneId = city.PrimaryTimeZoneId,
                AlternateTimeZoneId = city.SecondaryTimeZoneId
            })
            .ToList();
    }

    private static void ApplyClockCardTheme(ClockCityViewModel city, DateTimeOffset cityTime)
    {
        int hour = cityTime.Hour;
        (Color background, Color border) = hour switch
        {
            >= 5 and < 10 => (Color.FromArgb(0x86, 0x4B, 0x31, 0x10), Color.FromArgb(0x88, 0xD3, 0xA0, 0x53)),
            >= 10 and < 17 => (Color.FromArgb(0x82, 0x16, 0x35, 0x54), Color.FromArgb(0x88, 0x6F, 0xB8, 0xF2)),
            >= 17 and < 21 => (Color.FromArgb(0x82, 0x4A, 0x24, 0x1B), Color.FromArgb(0x88, 0xEE, 0x9A, 0x5C)),
            _ => (Color.FromArgb(0x78, 0x14, 0x1D, 0x2A), Color.FromArgb(0x76, 0x4D, 0x6B, 0x85))
        };

        city.CardBackground = new SolidColorBrush(background);
        city.CardBorderBrush = new SolidColorBrush(border);
    }

    private void LoadBackground(string? path)
    {
        if (!IsSupportedBackgroundReference(path))
        {
            _backgroundZoomTimer.Stop();
            if (_activeBackgroundImage is not null)
                _activeBackgroundImage.Source = null;
            if (_inactiveBackgroundImage is not null)
                _inactiveBackgroundImage.Source = null;
            return;
        }

        string backgroundPath = path!;

        if (_activeBackgroundImage is null || _inactiveBackgroundImage is null)
        {
            BackgroundImageA.Source = CreateBackgroundBitmap(backgroundPath);
            BackgroundImageA.Opacity = 0.45d;
            BackgroundImageB.Source = null;
            BackgroundImageB.Opacity = 0d;
            _activeBackgroundImage = BackgroundImageA;
            _inactiveBackgroundImage = BackgroundImageB;
            ResetBackgroundZoomState();
            EnsureBackgroundSlowZoomRunning();
            return;
        }

        if (_activeBackgroundImage.Source is null || string.IsNullOrWhiteSpace(_currentBackgroundPath))
        {
            StopBackgroundAnimations(_activeBackgroundImage);
            StopBackgroundAnimations(_inactiveBackgroundImage);
            ResetBackgroundTransform(_activeBackgroundImage);
            ResetBackgroundTransform(_inactiveBackgroundImage);
            _activeBackgroundImage.Source = CreateBackgroundBitmap(backgroundPath);
            _activeBackgroundImage.Opacity = 0.45d;
            _inactiveBackgroundImage.Source = null;
            _inactiveBackgroundImage.Opacity = 0d;
            ResetBackgroundZoomState();
            EnsureBackgroundSlowZoomRunning();
            return;
        }

        BeginBackgroundTransition(backgroundPath);
    }

    private void ApplyDimOpacity(double opacity)
    {
        byte alpha = (byte)Math.Clamp(opacity * 255d, 0, 255);
        DimOverlay.Fill = new SolidColorBrush(Color.FromArgb(alpha, 0, 0, 0));
    }

    private double NextVelocity()
    {
        double magnitude = _settings.FloatingGraphVelocityMin;
        if (_settings.FloatingGraphVelocityMax > _settings.FloatingGraphVelocityMin)
        {
            magnitude += _random.NextDouble() * (_settings.FloatingGraphVelocityMax - _settings.FloatingGraphVelocityMin);
        }

        return _random.Next(0, 2) == 0 ? magnitude : -magnitude;
    }

    private static void CopyMotion(FloatingSpriteViewModel source, FloatingSpriteViewModel target)
    {
        target.X = source.X;
        target.Y = source.Y;
        target.VelocityX = source.VelocityX;
        target.VelocityY = source.VelocityY;
        if (source is FloatingGraphViewModel sourceGraph && target is FloatingGraphViewModel targetGraph)
        {
            targetGraph.NominalVelocityX = sourceGraph.NominalVelocityX;
            targetGraph.NominalVelocityY = sourceGraph.NominalVelocityY;
            targetGraph.RefreshTravelTargetY = sourceGraph.RefreshTravelTargetY;
        }
    }

    private IEnumerable<FloatingSpriteViewModel> EnumerateSprites()
    {
        foreach (FloatingGraphViewModel graph in _graphs.Where(graph => graph.IsVisible))
            yield return graph;

        if (_networkWaitingViewModel is not null)
            yield return _networkWaitingViewModel;
    }

    private void PlaceSprite(
        FloatingSpriteViewModel sprite,
        Rect placementBounds,
        double preferredX,
        double preferredY,
        ICollection<Rect> occupied)
    {
        Rect normalizedBounds = NormalizePlacementBounds(placementBounds, sprite);
        Point[] offsets =
        [
            new Point(0, 0),
            new Point(36, 0),
            new Point(-36, 0),
            new Point(72, 0),
            new Point(-72, 0),
            new Point(0, 28),
            new Point(0, -28),
            new Point(72, 28),
            new Point(-72, 28),
            new Point(72, -28),
            new Point(-72, -28),
            new Point(144, 0),
            new Point(-144, 0),
            new Point(0, 56),
            new Point(0, -56)
        ];

        Rect fallback = ClampSpriteRect(new Rect(preferredX, preferredY, sprite.Width, sprite.Height), normalizedBounds);
        foreach (Point offset in offsets)
        {
            Rect candidate = ClampSpriteRect(
                new Rect(preferredX + offset.X, preferredY + offset.Y, sprite.Width, sprite.Height),
                normalizedBounds);

            if (!IntersectsOccupied(candidate, occupied))
            {
                ApplySpriteRect(sprite, candidate);
                occupied.Add(candidate);
                return;
            }
        }

        ApplySpriteRect(sprite, fallback);
        occupied.Add(fallback);
    }

    private void ResolveSpriteOverlaps(Rect bounds)
    {
        List<FloatingSpriteViewModel> sprites = EnumerateSprites()
            .Where(sprite => sprite.Width > 0 && sprite.Height > 0)
            .ToList();

        for (int iteration = 0; iteration < 6; iteration++)
        {
            bool movedAny = false;
            for (int i = 0; i < sprites.Count; i++)
            {
                for (int j = i + 1; j < sprites.Count; j++)
                {
                    Rect firstRect = GetSpriteRect(sprites[i]);
                    Rect secondRect = GetSpriteRect(sprites[j]);
                    if (!firstRect.IntersectsWith(secondRect))
                        continue;

                    double overlapX = Math.Min(firstRect.Right, secondRect.Right) - Math.Max(firstRect.Left, secondRect.Left);
                    double overlapY = Math.Min(firstRect.Bottom, secondRect.Bottom) - Math.Max(firstRect.Top, secondRect.Top);
                    if (overlapX <= 0 || overlapY <= 0)
                        continue;

                    if (overlapX <= overlapY)
                    {
                        double push = overlapX / 2d + 10d;
                        bool firstIsLeft = firstRect.Left <= secondRect.Left;
                        sprites[i].X += firstIsLeft ? -push : push;
                        sprites[j].X += firstIsLeft ? push : -push;
                        sprites[i].VelocityX = firstIsLeft ? -Math.Abs(sprites[i].VelocityX) : Math.Abs(sprites[i].VelocityX);
                        sprites[j].VelocityX = firstIsLeft ? Math.Abs(sprites[j].VelocityX) : -Math.Abs(sprites[j].VelocityX);
                    }
                    else
                    {
                        double push = overlapY / 2d + 10d;
                        bool firstIsTop = firstRect.Top <= secondRect.Top;
                        sprites[i].Y += firstIsTop ? -push : push;
                        sprites[j].Y += firstIsTop ? push : -push;
                        sprites[i].VelocityY = firstIsTop ? -Math.Abs(sprites[i].VelocityY) : Math.Abs(sprites[i].VelocityY);
                        sprites[j].VelocityY = firstIsTop ? Math.Abs(sprites[j].VelocityY) : -Math.Abs(sprites[j].VelocityY);
                    }

                    ClampSpriteToBounds(sprites[i], bounds);
                    ClampSpriteToBounds(sprites[j], bounds);
                    movedAny = true;
                }
            }

            if (!movedAny)
                break;
        }
    }

    private static Rect NormalizePlacementBounds(Rect placementBounds, FloatingSpriteViewModel sprite)
    {
        double width = Math.Max(sprite.Width + 4, placementBounds.Width);
        double height = Math.Max(sprite.Height + 4, placementBounds.Height);
        return new Rect(placementBounds.Left, placementBounds.Top, width, height);
    }

    private static Rect ClampSpriteRect(Rect spriteRect, Rect bounds)
    {
        double maxX = Math.Max(bounds.Left, bounds.Right - spriteRect.Width);
        double maxY = Math.Max(bounds.Top, bounds.Bottom - spriteRect.Height);
        double x = Math.Max(bounds.Left, Math.Min(maxX, spriteRect.X));
        double y = Math.Max(bounds.Top, Math.Min(maxY, spriteRect.Y));
        return new Rect(x, y, spriteRect.Width, spriteRect.Height);
    }

    private static bool IntersectsOccupied(Rect candidate, IEnumerable<Rect> occupied)
        => occupied.Any(existing => existing.IntersectsWith(candidate));

    private static Rect GetSpriteRect(FloatingSpriteViewModel sprite)
        => new(sprite.X, sprite.Y, Math.Max(1, sprite.Width), Math.Max(1, sprite.Height));

    private static void ApplySpriteRect(FloatingSpriteViewModel sprite, Rect rect)
    {
        sprite.X = rect.X;
        sprite.Y = rect.Y;
    }

    private static void ClampSpriteToBounds(FloatingSpriteViewModel sprite, Rect bounds)
    {
        Rect clamped = ClampSpriteRect(GetSpriteRect(sprite), bounds);
        ApplySpriteRect(sprite, clamped);
    }

    private void ApplyRefreshMotionCue(FloatingGraphViewModel graph, decimal? percent)
    {
        if (percent is null || percent == 0m)
            return;

        Rect bounds = GetGraphMotionBounds();
        if (bounds == Rect.Empty)
            return;

        graph.FlashBrush = percent > 0m ? Brushes.LimeGreen : Brushes.OrangeRed;
        graph.IsRefreshTravelFlashActive = true;
        graph.RefreshTravelTargetY = percent > 0m
            ? bounds.Top
            : Math.Max(bounds.Top, bounds.Bottom - Math.Max(1d, graph.Height));
    }

    private void ClampSpritesToSafeBounds()
    {
        Rect graphBounds = GetGraphMotionBounds();
        if (graphBounds != Rect.Empty)
        {
            foreach (FloatingGraphViewModel graph in _graphs)
                ClampSpriteToBounds(graph, graphBounds);
        }

        if (_networkWaitingViewModel is not null && graphBounds != Rect.Empty)
            ClampSpriteToBounds(_networkWaitingViewModel, graphBounds);
    }

    private void RotateBackground(bool forceDifferent = true)
    {
        if (_backgroundPaths.Count == 0)
        {
            LoadBackground(null);
            _currentBackgroundPath = null;
            return;
        }

        List<string> candidates = forceDifferent && !string.IsNullOrWhiteSpace(_currentBackgroundPath)
            ? _backgroundPaths.Where(path => !string.Equals(path, _currentBackgroundPath, StringComparison.OrdinalIgnoreCase)).ToList()
            : _backgroundPaths;

        if (candidates.Count == 0)
            candidates = _backgroundPaths;

        string nextPath = _settings.ShuffleBackgrounds
            ? candidates[_random.Next(candidates.Count)]
            : candidates[0];

        _currentBackgroundPath = nextPath;
        LoadBackground(nextPath);
    }

    private void BeginBackgroundTransition(string path)
    {
        if (_activeBackgroundImage is null || _inactiveBackgroundImage is null)
            return;

        Image incoming = _inactiveBackgroundImage;
        Image outgoing = _activeBackgroundImage;
        StopBackgroundAnimations(incoming);
        StopBackgroundAnimations(outgoing);

        incoming.Source = CreateBackgroundBitmap(path);
        incoming.Opacity = 0d;
        ResetBackgroundTransform(incoming);
        ResetBackgroundTransform(outgoing);
        SetBackgroundScale(incoming, _backgroundZoomScale, _backgroundZoomScale);

        IEasingFunction ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        TimeSpan duration = TimeSpan.FromMilliseconds(1200);
        AnimateBackgroundProperty(incoming, Image.OpacityProperty, 0d, 0.45d, duration, ease);
        AnimateBackgroundProperty(outgoing, Image.OpacityProperty, outgoing.Opacity, 0d, duration, ease);

        DispatcherTimer completionTimer = new() { Interval = duration + TimeSpan.FromMilliseconds(100) };
        completionTimer.Tick += (_, _) =>
        {
            completionTimer.Stop();
            ResetBackgroundTransform(incoming);
            ResetBackgroundTransform(outgoing);
            SetBackgroundScale(incoming, _backgroundZoomScale, _backgroundZoomScale);
            incoming.Opacity = 0.45d;
            outgoing.Opacity = 0d;
            outgoing.Source = null;
            _activeBackgroundImage = incoming;
            _inactiveBackgroundImage = outgoing;
            EnsureBackgroundSlowZoomRunning();
        };
        completionTimer.Start();
    }

    private void ResetBackgroundZoomState()
    {
        _backgroundZoomScale = 1.01d;
        _backgroundZoomDirection = 1d;
        if (_activeBackgroundImage is not null)
            SetBackgroundScale(_activeBackgroundImage, _backgroundZoomScale, _backgroundZoomScale);
    }

    private void EnsureBackgroundSlowZoomRunning()
    {
        if (_activeBackgroundImage?.Source is null)
        {
            _backgroundZoomTimer.Stop();
            return;
        }

        if (!_backgroundZoomTimer.IsEnabled)
            _backgroundZoomTimer.Start();
    }

    private void StepBackgroundSlowZoom()
    {
        if (_activeBackgroundImage?.Source is null)
        {
            _backgroundZoomTimer.Stop();
            return;
        }

        const double minScale = 1.00d;
        const double maxScale = 1.05d;
        const double step = 0.00075d;

        _backgroundZoomScale += step * _backgroundZoomDirection;
        if (_backgroundZoomScale >= maxScale)
        {
            _backgroundZoomScale = maxScale;
            _backgroundZoomDirection = -1d;
        }
        else if (_backgroundZoomScale <= minScale)
        {
            _backgroundZoomScale = minScale;
            _backgroundZoomDirection = 1d;
        }

        SetBackgroundScale(_activeBackgroundImage, _backgroundZoomScale, _backgroundZoomScale);
    }

    private static BitmapImage CreateBackgroundBitmap(string path)
    {
        BitmapImage bitmap = new();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static bool IsSupportedBackgroundReference(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        if (File.Exists(path))
            return true;

        return Uri.TryCreate(path, UriKind.Absolute, out Uri? uri) &&
               (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    }

    private void ApplyGraphRefreshImpulse(FloatingGraphViewModel graph, Rect bounds)
    {
        if (graph.RefreshTravelTargetY is not double targetY)
            return;

        double nominalX = graph.NominalVelocityX == 0d ? NextVelocity() : graph.NominalVelocityX;
        double nominalY = graph.NominalVelocityY == 0d ? NextVelocity() : graph.NominalVelocityY;
        graph.NominalVelocityX = nominalX;
        graph.NominalVelocityY = nominalY;
        graph.VelocityX = 0d;
        graph.VelocityY = targetY <= bounds.Top + 1d
            ? -Math.Max(12d, Math.Abs(nominalY) * 2d)
            : Math.Max(12d, Math.Abs(nominalY) * 2d);
    }

    private static void ResetGraphRefreshImpulseIfNeeded(FloatingGraphViewModel graph, Rect bounds)
    {
        if (graph.RefreshTravelTargetY is not double targetY)
            return;

        bool hitBoundary = targetY <= bounds.Top + 1d
            ? graph.Y <= bounds.Top + 1d
            : graph.Y >= Math.Max(bounds.Top, bounds.Bottom - Math.Max(1d, graph.Height)) - 1d;
        if (!hitBoundary)
            return;

        graph.RefreshTravelTargetY = null;
        graph.IsRefreshTravelFlashActive = false;
        graph.VelocityX = graph.NominalVelocityX == 0d ? graph.VelocityX : graph.NominalVelocityX;
        graph.VelocityY = graph.NominalVelocityY == 0d
            ? graph.VelocityY
            : (targetY <= bounds.Top + 1d
                ? Math.Abs(graph.NominalVelocityY)
                : -Math.Abs(graph.NominalVelocityY));
    }

    private static void StopBackgroundAnimations(Image image)
    {
        image.BeginAnimation(UIElement.OpacityProperty, null);
        if (image.RenderTransform is not TransformGroup transformGroup)
            return;

        if (transformGroup.Children.OfType<ScaleTransform>().FirstOrDefault() is ScaleTransform scale)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        }

        if (transformGroup.Children.OfType<TranslateTransform>().FirstOrDefault() is TranslateTransform translate)
        {
            translate.BeginAnimation(TranslateTransform.XProperty, null);
            translate.BeginAnimation(TranslateTransform.YProperty, null);
        }
    }

    private static void ResetBackgroundTransform(Image image)
    {
        SetBackgroundScale(image, 1d, 1d);
        SetBackgroundTranslation(image, 0d, 0d);
    }

    private static void SetBackgroundScale(Image image, double scaleX, double scaleY)
    {
        if (image.RenderTransform is not TransformGroup transformGroup)
            return;

        if (transformGroup.Children.OfType<ScaleTransform>().FirstOrDefault() is ScaleTransform scale)
        {
            scale.ScaleX = scaleX;
            scale.ScaleY = scaleY;
        }
    }

    private static void SetBackgroundTranslation(Image image, double x, double y)
    {
        if (image.RenderTransform is not TransformGroup transformGroup)
            return;

        if (transformGroup.Children.OfType<TranslateTransform>().FirstOrDefault() is TranslateTransform translate)
        {
            translate.X = x;
            translate.Y = y;
        }
    }

    private static void AnimateBackgroundProperty(
        Image image,
        DependencyProperty property,
        double from,
        double to,
        TimeSpan duration,
        IEasingFunction ease)
    {
        DoubleAnimation animation = new(from, to, duration)
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.HoldEnd
        };
        image.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private static void AnimateBackgroundScale(Image image, double fromScale, double toScale, TimeSpan duration, IEasingFunction ease)
    {
        if (image.RenderTransform is not TransformGroup transformGroup)
            return;

        if (transformGroup.Children.OfType<ScaleTransform>().FirstOrDefault() is not ScaleTransform scale)
            return;

        DoubleAnimation xAnimation = new(fromScale, toScale, duration)
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.HoldEnd
        };
        DoubleAnimation yAnimation = new(fromScale, toScale, duration)
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.HoldEnd
        };

        scale.BeginAnimation(ScaleTransform.ScaleXProperty, xAnimation, HandoffBehavior.SnapshotAndReplace);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, yAnimation, HandoffBehavior.SnapshotAndReplace);
    }

    private static void AnimateBackgroundTranslation(
        Image image,
        double fromX,
        double toX,
        double fromY,
        double toY,
        TimeSpan duration,
        IEasingFunction ease)
    {
        if (image.RenderTransform is not TransformGroup transformGroup)
            return;

        if (transformGroup.Children.OfType<TranslateTransform>().FirstOrDefault() is not TranslateTransform translate)
            return;

        DoubleAnimation xAnimation = new(fromX, toX, duration)
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.HoldEnd
        };
        DoubleAnimation yAnimation = new(fromY, toY, duration)
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.HoldEnd
        };

        translate.BeginAnimation(TranslateTransform.XProperty, xAnimation, HandoffBehavior.SnapshotAndReplace);
        translate.BeginAnimation(TranslateTransform.YProperty, yAnimation, HandoffBehavior.SnapshotAndReplace);
    }

    private void ApplyNetworkWaitingOverlay(ScreensaverSceneState state)
    {
        if (!state.ShowNetworkWaitingOverlay)
        {
            NetworkWaitingHost.Visibility = Visibility.Collapsed;
            NetworkWaitingHost.Content = null;
            NetworkWaitingHost.DataContext = null;
            _networkWaitingViewModel = null;
            return;
        }

        _networkWaitingViewModel ??= new NetworkWaitingViewModel
        {
            Width = 340,
            Height = 110,
            BounceWithinViewport = true
        };

        _networkWaitingViewModel.TitleText = state.NetworkWaitingTitle ?? "Waiting for network";
        _networkWaitingViewModel.DetailText = state.NetworkWaitingDetail ?? "Retrying live quotes soon.";
        NetworkWaitingHost.Visibility = Visibility.Visible;
        NetworkWaitingHost.DataContext = _networkWaitingViewModel;
        NetworkWaitingHost.Content = _networkWaitingViewModel;
        NetworkWaitingHost.SetBinding(Canvas.LeftProperty, new Binding(nameof(NetworkWaitingViewModel.X)));
        NetworkWaitingHost.SetBinding(Canvas.TopProperty, new Binding(nameof(NetworkWaitingViewModel.Y)));
    }

    private static string GetGraphKey(FloatingGraphViewModel graph) => $"{graph.TapeName}|{graph.Symbol}";

    private void SyncTapes(IReadOnlyList<TapeViewModel> sourceTapes)
    {
        bool structureChanged = _tapes.Count != sourceTapes.Count;
        if (!structureChanged)
        {
            for (int i = 0; i < sourceTapes.Count; i++)
            {
                if (!string.Equals(_tapes[i].Title, sourceTapes[i].Title, StringComparison.Ordinal) ||
                    _tapes[i].Direction != sourceTapes[i].Direction ||
                    Math.Abs(_tapes[i].Speed - sourceTapes[i].Speed) > 0.001d)
                {
                    structureChanged = true;
                    break;
                }
            }
        }

        if (structureChanged)
        {
            _tapes.Clear();
            foreach (TapeViewModel sourceTape in sourceTapes)
            {
                TapeViewModel clone = new()
                {
                    Title = sourceTape.Title,
                    Direction = sourceTape.Direction,
                    Speed = sourceTape.Speed,
                    Items = []
                };

                SyncTapeItems(clone.Items, sourceTape.Items);
                _tapes.Add(clone);
            }

            return;
        }

        for (int i = 0; i < sourceTapes.Count; i++)
        {
            _tapes[i].Title = sourceTapes[i].Title;
            _tapes[i].Direction = sourceTapes[i].Direction;
            _tapes[i].Speed = sourceTapes[i].Speed;
            SyncTapeItems(_tapes[i].Items, sourceTapes[i].Items);
        }
    }

    private static void SyncTapeItems(ObservableCollection<TapeItemViewModel> target, IEnumerable<TapeItemViewModel> source)
    {
        List<TapeItemViewModel> sourceItems = source.ToList();
        while (target.Count > sourceItems.Count)
            target.RemoveAt(target.Count - 1);

        for (int index = 0; index < sourceItems.Count; index++)
        {
            TapeItemViewModel next = sourceItems[index];
            if (index < target.Count)
            {
                UpdateTapeItem(target[index], next);
            }
            else
            {
                target.Add(next);
            }
        }
    }

    private void SyncNews(NewsFlasherViewModel source)
    {
        _newsViewModel.Title = source.Title;
        _newsViewModel.Speed = source.Speed;

        List<NewsHeadlineViewModel> sourceItems = source.Headlines.ToList();
        while (_newsViewModel.Headlines.Count > sourceItems.Count)
            _newsViewModel.Headlines.RemoveAt(_newsViewModel.Headlines.Count - 1);

        for (int index = 0; index < sourceItems.Count; index++)
        {
            NewsHeadlineViewModel next = sourceItems[index];
            if (index < _newsViewModel.Headlines.Count)
                UpdateHeadline(_newsViewModel.Headlines[index], next);
            else
                _newsViewModel.Headlines.Add(CloneHeadline(next));
        }

        _newsViewModel.MarqueeText = string.Join(" | ", _newsViewModel.Headlines.Select(headline => headline.Text));
    }

    private static TapeItemViewModel CloneTapeItem(TapeItemViewModel item) => new()
    {
        SymbolText = item.SymbolText,
        LastText = item.LastText,
        ChangeText = item.ChangeText,
        IsWaitingOnData = item.IsWaitingOnData,
        HasMissingData = item.HasMissingData,
        WaitingGlyphText = item.WaitingGlyphText,
        WaitingGlyphForeground = item.WaitingGlyphForeground,
        SymbolForeground = item.SymbolForeground,
        LastForeground = item.LastForeground,
        ChangeForeground = item.ChangeForeground
    };

    private static void UpdateTapeItem(TapeItemViewModel target, TapeItemViewModel source)
    {
        bool hadPriorSymbol = !string.IsNullOrWhiteSpace(target.SymbolText);
        bool valueChanged = !string.Equals(target.LastText, source.LastText, StringComparison.Ordinal) ||
                            !string.Equals(target.ChangeText, source.ChangeText, StringComparison.Ordinal);
        bool updateTokenChanged = source.QuoteUpdateToken > 0 && source.QuoteUpdateToken != target.QuoteUpdateToken;

        target.SymbolText = source.SymbolText;
        target.LastText = source.LastText;
        target.ChangeText = source.ChangeText;
        target.IsWaitingOnData = source.IsWaitingOnData;
        target.HasMissingData = source.HasMissingData;
        target.WaitingGlyphText = source.WaitingGlyphText;
        target.WaitingGlyphForeground = source.WaitingGlyphForeground;
        target.SymbolForeground = source.SymbolForeground;
        target.LastForeground = source.LastForeground;
        target.ChangeForeground = source.ChangeForeground;
        target.ValueFlashBrush = source.ValueFlashBrush;
        target.QuoteUpdateToken = source.QuoteUpdateToken;

        if (hadPriorSymbol && (valueChanged || updateTokenChanged) && !string.IsNullOrWhiteSpace(source.LastText))
            target.TriggerValueFlash(source.ChangeForeground);
    }

    private void TraceDisplayedTapeSample()
    {
        List<string> sample = _tapes
            .SelectMany(tape => tape.Items)
            .Where(item => !string.IsNullOrWhiteSpace(item.SymbolText))
            .GroupBy(item => item.SymbolText, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(8)
            .Select(item =>
            {
                string state = item.HasMissingData
                    ? "missing"
                    : item.IsWaitingOnData
                        ? "stale"
                        : "live";

                return $"{item.SymbolText}~{NormalizeTapeSnapshotValue(item.LastText)}~{NormalizeTapeSnapshotValue(item.ChangeText)}~{state}";
            })
            .ToList();

        if (sample.Count == 0)
            return;

        TraceSceneState(
            "DisplayedTapeSample",
            new KeyValuePair<string, object?>("sample_count", sample.Count),
            new KeyValuePair<string, object?>("sample", sample));
    }

    private static string NormalizeTapeSnapshotValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();

    private static NewsHeadlineViewModel CloneHeadline(NewsHeadlineViewModel item) => new()
    {
        Text = item.Text,
        Foreground = item.Foreground,
        IsSupplemental = item.IsSupplemental
    };

    private static void UpdateHeadline(NewsHeadlineViewModel target, NewsHeadlineViewModel source)
    {
        target.Text = source.Text;
        target.Foreground = source.Foreground;
        target.IsSupplemental = source.IsSupplemental;
    }

    private void ApplyQuotesToGraphs()
    {
        foreach (FloatingGraphViewModel graph in _graphs)
            ApplyQuoteToGraph(graph);
    }

    private void ApplyQuoteToGraph(FloatingGraphViewModel graph)
    {
        if (!_latestQuotes.TryGetValue(graph.Symbol, out QuoteSnapshot? quote))
        {
            graph.IsVisible = graph.Points.Count > 1 || graph.GreenSegments.Count > 0 || graph.RedSegments.Count > 0;
            return;
        }

        if (IsQuoteBeyondStaleThreshold(quote))
        {
            graph.IsVisible = false;
            graph.LastText = string.Empty;
            graph.ChangeText = string.Empty;
            graph.ChangeForeground = Brushes.Gainsboro;
            return;
        }

        graph.IsVisible = true;

        decimal? last = quote.Last ?? quote.PreviousClose;
        decimal? percent = quote.ChangePercent;
        string lastText = last is decimal lastValue ? lastValue.ToString("0.00") : string.Empty;
        string changeText = percent is decimal percentValue
            ? $"{(percentValue >= 0 ? "+" : string.Empty)}{percentValue:0.00}%"
            : string.Empty;
        Brush changeBrush = percent switch
        {
            > 0m => Brushes.LimeGreen,
            < 0m => Brushes.OrangeRed,
            _ => Brushes.Gainsboro
        };

        bool hadPriorSymbol = !string.IsNullOrWhiteSpace(graph.Symbol);
        bool valueChanged = !string.Equals(graph.LastText, lastText, StringComparison.Ordinal) ||
                            !string.Equals(graph.ChangeText, changeText, StringComparison.Ordinal);
        long quoteUpdateToken = quote.FetchTimestampUtc.UtcTicks;
        bool updateTokenChanged = quoteUpdateToken > 0 && quoteUpdateToken != graph.QuoteUpdateToken;

        graph.LastText = lastText;
        graph.ChangeText = changeText;
        graph.ChangeForeground = changeBrush;
        graph.LatestSegmentBrush = changeBrush;
        graph.QuoteUpdateToken = quoteUpdateToken;

        if (hadPriorSymbol && (valueChanged || updateTokenChanged) && !string.IsNullOrWhiteSpace(lastText))
        {
            ApplyRefreshMotionCue(graph, percent);
            graph.TriggerCardFlash(changeBrush);
        }
    }

    private bool IsQuoteBeyondStaleThreshold(QuoteSnapshot quote)
    {
        if (quote.IsStale)
            return true;

        DateTimeOffset nowUtc = GetReferenceUtcNow();
        return nowUtc - quote.FetchTimestampUtc >= QuoteRefreshPolicy.GetHardStaleThreshold(_settings, nowUtc);
    }

    private void UpdateStatusFreshnessText(string? fallbackText = null)
    {
        if (_statusViewModel is null)
            return;

        if (StartupCoordinator.ShouldShowInitialValueLoadingStatus(_latestQuotes, _settings, GetReferenceUtcNow()))
        {
            bool showMessage = GetReferenceUtcNow().Second % 2 == 0;
            _statusViewModel.UpdatedText = showMessage ? "Loading initial values" : string.Empty;
            return;
        }

        if (StartupCoordinator.TryGetStatusFreshnessAnchorFetchUtc(_latestQuotes, out DateTimeOffset anchorQuoteFetchUtc))
        {
            _statusViewModel.UpdatedText = $"Updated: {TimeFormatHelper.ToAgeString(anchorQuoteFetchUtc)}";
            return;
        }

        if (!string.IsNullOrWhiteSpace(fallbackText))
            _statusViewModel.UpdatedText = fallbackText;
    }

    private static IReadOnlyDictionary<string, QuoteSnapshot> MergeQuotes(
        IReadOnlyDictionary<string, QuoteSnapshot> existing,
        IReadOnlyDictionary<string, QuoteSnapshot> incoming)
    {
        Dictionary<string, QuoteSnapshot> merged = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string symbol, QuoteSnapshot quote) in existing)
            merged[symbol] = quote;

        foreach ((string symbol, QuoteSnapshot quote) in incoming)
            merged[symbol] = quote;

        return merged;
    }

    private void CompactStatusText()
    {
        if (_statusViewModel is null)
            return;

        _statusViewModel.ProviderText = CompactProviderText(_statusViewModel.ProviderText);
    }

    private static string CompactProviderText(string providerText)
    {
        if (string.IsNullOrWhiteSpace(providerText) ||
            !providerText.StartsWith("Provider:", StringComparison.OrdinalIgnoreCase))
        {
            return providerText;
        }

        string body = providerText["Provider:".Length..].Trim();
        int commaIndex = body.LastIndexOf(',');
        if (commaIndex >= 0)
        {
            string candidate = body[(commaIndex + 1)..].Trim();
            if (candidate.Contains("Updated", StringComparison.OrdinalIgnoreCase))
                body = body[..commaIndex].Trim();
        }

        bool includesCache = body.Contains("Cache", StringComparison.OrdinalIgnoreCase);
        bool isPartial = body.Contains("Partial", StringComparison.OrdinalIgnoreCase);
        bool localOnly = body.Contains("Local Cache", StringComparison.OrdinalIgnoreCase);
        bool cooldown = body.Contains("cooldown", StringComparison.OrdinalIgnoreCase);

        string compact = body switch
        {
            _ when localOnly && cooldown => "Cache cooldown",
            _ when localOnly => "Local cache",
            _ when body.Contains(" + ", StringComparison.Ordinal) => includesCache ? "Live+Cache" : "Live",
            _ => body
        };

        compact = compact
            .Replace("Yahoo Finance v8", "Yahoo", StringComparison.OrdinalIgnoreCase)
            .Replace("Twelve Data", "Twelve", StringComparison.OrdinalIgnoreCase)
            .Replace("Financial Modeling Prep", "FMP", StringComparison.OrdinalIgnoreCase)
            .Replace(" source cooldown", " cooldown", StringComparison.OrdinalIgnoreCase);

        if (isPartial && !compact.Contains("Partial", StringComparison.OrdinalIgnoreCase))
            compact += " (Partial)";

        return $"Provider: {compact}";
    }

    private void StartDemoFlashSequence()
    {
        _demoFlashTicks = 0;
        if (_demoFlashTimer.IsEnabled)
            _demoFlashTimer.Stop();

        _demoFlashTimer.Start();
        RunDemoFlashPulse();
    }

    private void StopDemoFlashSequence()
    {
        _demoFlashTimer.Stop();
        _demoFlashTicks = 0;
    }

    private void RunDemoFlashPulse()
    {
        if (_demoFlashTicks >= 4)
        {
            _demoFlashTimer.Stop();
            return;
        }

        _demoFlashTicks++;
        if (_tapes.Count > 0 && _tapes[0].Items.Count > 0)
            _tapes[0].Items[0].TriggerValueFlash(Brushes.DeepSkyBlue);

        if (_graphs.Count > 0)
            _graphs[0].TriggerCardFlash(Brushes.DeepSkyBlue);
    }

    private void SyncGraphVisuals()
    {
        FloatingGraphCanvas.Children.Clear();

        foreach (FloatingGraphViewModel graph in EnumerateVisibleGraphCards())
        {
            FloatingGraphControl control = new()
            {
                DataContext = graph
            };

            control.SetBinding(Canvas.LeftProperty, new Binding(nameof(FloatingGraphViewModel.X)));
            control.SetBinding(Canvas.TopProperty, new Binding(nameof(FloatingGraphViewModel.Y)));
            Panel.SetZIndex(control, 12);
            FloatingGraphCanvas.Children.Add(control);
        }
    }
}






