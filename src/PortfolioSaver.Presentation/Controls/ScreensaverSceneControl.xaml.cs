using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Automation;
using PortfolioSaver.Data.Interfaces;
using PortfolioSaver.Data.Providers;
using PortfolioSaver.Data.Services;
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
    private const int RuntimeQuoteOfflineFailureThreshold = 10;
    private static readonly bool EnableMarketCritters = false;
    private const string PinnedNycExchangeKey = "NewYorkNasdaq";
    private const int MaxVisibleGraphCards = 16;
    private const string FooterBaseText = "\u00A9 Supratim Sanyal. MIT License.";
    private static readonly TimeSpan GraphSelectionRefreshInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan MacroLaneMinimumRefreshInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan WorldMarketsLaneMinimumRefreshInterval = TimeSpan.FromSeconds(15);
    // Runtime quotes intentionally use a fixed one-at-a-time transport cadence:
    // dispatch a single symbol, apply that response surgically, then wait for the
    // next timer tick before dispatching another symbol. This avoids UI bursts.
    private static readonly TimeSpan RuntimeQuoteDispatchInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RuntimeQuoteRequestTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan RuntimeTapeStructuralSyncInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan GraphRefreshTravelFlashMaximumDuration = TimeSpan.FromSeconds(8);
    private readonly ObservableCollection<FloatingGraphViewModel> _graphs = [];
    private readonly Dictionary<string, FloatingGraphControl> _graphControlsByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly ObservableCollection<MarketSpriteViewModel> _marketSprites = [];
    private readonly ObservableCollection<TapeViewModel> _tapes = [];
    private readonly StartupCoordinator _startupCoordinator = new();
    private readonly FloatingSpriteMotionController _motionController = new();
    private readonly YFinanceExchangeTimingService _exchangeMarketCalendarService = new();
    private readonly DispatcherTimer _clockTimer = new();
    private readonly DispatcherTimer _refreshTimer = new();
    private readonly DispatcherTimer _backgroundTimer = new();
    private readonly DispatcherTimer _backgroundZoomTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly DispatcherTimer _worldDataTimer = new();
    private DispatcherTimer? _backgroundTransitionCompletionTimer;
    private bool _backgroundTransitionInFlight;
    private bool _backgroundRecoveryReloadInFlight;
    private int _backgroundRecoveryReloadGeneration;
    private int _backgroundTransitionGeneration;
    private readonly DispatcherTimer _demoFlashTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly DispatcherTimer _motionTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Random _random = new();
    private readonly WorldWeatherService _worldWeatherService = new();
    private readonly NtpTimeService _ntpTimeService = new();
    private readonly NetworkAvailabilityService _networkAvailabilityService = new();
    private readonly HttpClient _runtimeQuoteHttpClient = HttpClientFactory.Create(TimeSpan.FromSeconds(5));
    private readonly IQuoteProvider _runtimeQuoteProvider;
    private Image? _activeBackgroundImage;
    private Image? _inactiveBackgroundImage;
    private BitmapImage? _currentBackgroundBitmap;
    private ImageSource? _committedBackgroundSource;

    private AppSettings _settings = new();
    private FloatingClockViewModel? _clockViewModel;
    private NetworkWaitingViewModel? _networkWaitingViewModel;
    private StatusBarViewModel? _statusViewModel;
    private readonly NewsFlasherViewModel _newsViewModel = new();
    private List<string> _backgroundPaths = [];
    private IReadOnlyDictionary<string, string> _backgroundAttributions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, QuoteSnapshot> _latestQuotes = new Dictionary<string, QuoteSnapshot>(StringComparer.OrdinalIgnoreCase);
    private string? _currentBackgroundPath;
    private DateTime _lastMotionTick = DateTime.UtcNow;
    private bool _initialized;
    private bool _isRefreshing;
    private bool _hasSeededLayout;
    private int _graphRotationSeed;
    private CancellationTokenSource? _graphWarmupCancellation;
    private CancellationTokenSource? _backgroundRecoveryReloadCancellation;
    private Task? _graphWarmupTask;
    private CancellationTokenSource? _captureSequenceCancellation;
    private TimeSpan? _ntpOffset;
    private DateTimeOffset _lastNtpSyncUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _lastWeatherRefreshUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _lastMarketCalendarRefreshUtc = DateTimeOffset.MinValue;
    private IReadOnlyDictionary<string, WeatherSnapshot> _weatherSnapshots = new Dictionary<string, WeatherSnapshot>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<decimal>> _clockIndexHistory = new(StringComparer.OrdinalIgnoreCase);
    private ExchangeCalendarSet _exchangeCalendars = new();
    private double _backgroundZoomScale = 1.01d;
    private double _backgroundZoomDirection = 1d;
    private double _currentBackgroundOpacity = 0.45d;
    private int _demoFlashTicks;
    private DateTimeOffset _lastCritterTargetSwapUtc = DateTimeOffset.MinValue;
    private bool _crittersChasingDollar = true;
    private DateTimeOffset _lastMacroMeterRefreshUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _lastMacroTraceUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _lastWorldMarketsLaneRefreshUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _lastClockMarketTraceUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _lastStatusAncillaryRefreshUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _lastClockAncillaryRefreshUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _lastSceneHeartbeatUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _lastGraphSelectionRefreshUtc = DateTimeOffset.MinValue;
    private bool _isValidationPaused;
    private readonly RuntimeQuoteInFlightTracker<IReadOnlyList<QuoteSnapshot>> _inFlightQuoteRequests = new(StringComparer.OrdinalIgnoreCase);
    private List<string> _orderedRuntimeSymbols = [];
    private int _runtimeSymbolCursor;
    private int _runtimeQuoteFailureStreak;
    private DateTimeOffset _lastAllRuntimeQuotesInFlightTraceUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _lastRuntimeQuoteLoopHeartbeatUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _lastDisplayedTapeSampleTraceUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _lastFullTapeSyncUtc = DateTimeOffset.MinValue;
    private readonly RuntimeQuoteRecoveryGate _runtimeQuoteRecoveryGate = new(RuntimeQuoteOfflineFailureThreshold, TimeSpan.FromSeconds(30));
    private CancellationTokenSource? _newsRefreshCancellation;
    private Task? _newsRefreshTask;
    private int _newsRefreshInFlight;
    private readonly SemaphoreSlim _macroLaneSignal = new(0);
    private readonly SemaphoreSlim _worldMarketsLaneSignal = new(0);
    private CancellationTokenSource? _macroLaneCancellation;
    private Task? _macroLaneTask;
    private int _macroLaneDirty;
    private CancellationTokenSource? _worldMarketsLaneCancellation;
    private Task? _worldMarketsLaneTask;
    private int _worldMarketsQuoteDirty;
    private int _worldMarketsAncillaryDirty;

    public ScreensaverSceneControl()
    {
        InitializeComponent();
        _runtimeQuoteProvider = new YahooFinanceQuoteProvider(_runtimeQuoteHttpClient, throwOnPartial: false);
        if (VersionWatermark is not null)
        {
            VersionWatermark.Text = PortfolioVersion.SemanticVersion;
            AutomationProperties.SetAutomationId(VersionWatermark, "ScreensaverVersionWatermark");
            AutomationProperties.SetName(VersionWatermark, $"Version {PortfolioVersion.SemanticVersion}");
            AutomationProperties.SetHelpText(VersionWatermark, PortfolioVersion.SemanticVersion);
        }
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
        _refreshTimer.Tick += (_, _) => DispatchNextRuntimeQuoteRequestSafe();
        _backgroundTimer.Tick += async (_, _) => await RotateBackgroundAsync();
        _backgroundZoomTimer.Tick += (_, _) => StepBackgroundSlowZoom();
        _worldDataTimer.Tick += (_, _) => QueueWorldMarketsRefresh(refreshAncillary: true, reason: "timer");
        _startupCoordinator.BackgroundCacheWarmupCompleted += QueueBackgroundCatalogRescan;
        _demoFlashTimer.Tick += (_, _) => RunDemoFlashPulse();
        _motionTimer.Tick += (_, _) => StepMotion();
    }


    private void QueueBackgroundCatalogRescan()
    {
        if (!_initialized)
            return;

        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            (IReadOnlyList<string> paths, IReadOnlyDictionary<string, string> attributions) = _startupCoordinator.GetCurrentBackgroundCatalog();
            _backgroundAttributions = attributions;
            _backgroundPaths = paths
                .Where(IsSupportedBackgroundReference)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (string.IsNullOrWhiteSpace(_currentBackgroundPath) || !_backgroundPaths.Contains(_currentBackgroundPath, StringComparer.OrdinalIgnoreCase))
                _currentBackgroundPath = null;
            else
                UpdateFooterAttribution(_currentBackgroundPath);
            ConfigureTimers();
            TraceSceneState(
                "BackgroundCatalogRescanned",
                new KeyValuePair<string, object?>("background_count", _backgroundPaths.Count));
        }), DispatcherPriority.Background);
    }
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
            return;

        try
        {
            TraceScene("OnLoaded starting.");
            _initialized = true;
            ResetRuntimeQuoteFailureStreak();
            _activeBackgroundImage = BackgroundImageA;
            _inactiveBackgroundImage = BackgroundImageB;
            ApplySceneState(_startupCoordinator.BuildBootstrapScene(), preserveLayout: false);
            _refreshTimer.Stop();
            RestartGraphWarmup(_graphRotationSeed, preserveLayout: false);
            await RefreshSceneAsync(preserveLayout: false, fullAncillaryRefresh: true);
            InitializeRuntimeQuoteLoop();
            StartNewsRefreshLoop();
            StartMacroLane();
            StartWorldMarketsLane();
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
        _startupCoordinator.BackgroundCacheWarmupCompleted -= QueueBackgroundCatalogRescan;
        StopRuntimeQuoteLoop();
        CancelNewsRefreshLoop();
        CancelMacroLane();
        CancelWorldMarketsLane();
        _clockTimer.Stop();
        _refreshTimer.Stop();
        _backgroundTimer.Stop();
        _backgroundZoomTimer.Stop();
        CancelBackgroundRecoveryReload();
        _worldDataTimer.Stop();
        StopDemoFlashSequence();
        _motionTimer.Stop();
        CancelGraphWarmup();
        CancelCaptureSequence();
        _runtimeQuoteHttpClient.Dispose();
        _initialized = false;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyResponsiveLayout();
        if (!_hasSeededLayout && _graphs.Count > 0)
            SeedSpriteLayout(onlyMissingPositions: false);
    }

    private async Task RefreshSceneAsync(bool preserveLayout, bool fullAncillaryRefresh = false)
    {
        if (_isValidationPaused)
        {
            TraceScene("RefreshSceneAsync skipped because validation pause is active.");
            return;
        }

        if (preserveLayout && !fullAncillaryRefresh)
        {
            TraceScene("RefreshSceneAsync bypassed progressive quote scene because the async runtime quote loop owns ordinary quote cadence.");
            return;
        }

        if (_isRefreshing)
            return;

        _isRefreshing = true;
        try
        {
            int currentRotationSeed = _graphRotationSeed;
            ScreensaverSceneState state = await _startupCoordinator.BuildSceneAsync(currentRotationSeed);
            if (_isValidationPaused)
            {
                TraceScene("RefreshSceneAsync discarded fetched scene because validation pause became active.");
                return;
            }

            ApplySceneState(state, preserveLayout, fullAncillaryRefresh);
            if (!preserveLayout)
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

    private void ApplySceneState(ScreensaverSceneState state, bool preserveLayout, bool fullAncillaryRefresh = false)
    {
        TraceScene($"ApplySceneState preserveLayout={preserveLayout} tapes={state.Tapes.Count} graphs={state.Graphs.Count} backgrounds={state.BackgroundPaths.Count} news={state.News.Headlines.Count} waiting={state.ShowNetworkWaitingOverlay}");
        bool structuralRefresh = !preserveLayout;
        _settings = state.Settings;
        _latestQuotes = MergeQuotes(_latestQuotes, state.Quotes);
        _backgroundAttributions = state.BackgroundAttributions;
        SyncStatusViewModel(state.Status, forceMacroRefresh: structuralRefresh || fullAncillaryRefresh);
        SyncTapes(_startupCoordinator.BuildTapesForQuotes(_settings, _latestQuotes));
        if (structuralRefresh || NewsChanged(state.News))
            SyncNews(state.News);
        ApplyDimOpacity(state.Settings.DimOpacity);
        if (structuralRefresh)
        {
            _backgroundPaths = state.BackgroundPaths
                .Where(IsSupportedBackgroundReference)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (string.IsNullOrWhiteSpace(_currentBackgroundPath) || !_backgroundPaths.Contains(_currentBackgroundPath, StringComparer.OrdinalIgnoreCase))
                _currentBackgroundPath = null;
        }

        if (_currentBackgroundPath is null)
            _ = RotateBackgroundAsync(forceDifferent: false);

        ApplyNetworkWaitingOverlay(state);

        bool graphStructureChanged = false;
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

            graphStructureChanged = true;
        }

        if (graphStructureChanged || structuralRefresh)
            SyncGraphVisuals();
        ApplyQuotesToGraphs();

        if (structuralRefresh)
        {
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
        }
        else if (_clockViewModel is null && state.Clock is not null)
        {
            _clockViewModel = state.Clock;
            GlobalMarketsTapeHost.Visibility = Visibility.Visible;
            GlobalMarketsTapeHost.DataContext = _clockViewModel;
            GlobalMarketsTapeHost.Content = _clockViewModel;
        }

        if (structuralRefresh)
        {
            UpdateLayout();
            _hasSeededLayout = preserveLayout && _graphs.All(graph => graph.X > 0 || graph.Y > 0);
            ApplyResponsiveLayout();
            SeedSpriteLayout(onlyMissingPositions: preserveLayout);
            RefreshOrderedRuntimeSymbols();
        }

        UpdateClocks(forceAncillaryRefresh: structuralRefresh || fullAncillaryRefresh);
        ApplyWeatherToClock();
        if (structuralRefresh)
        {
            ConfigureTimers();
            QueueWorldMarketsRefresh(refreshAncillary: true, reason: "structural-refresh");
        }
        TraceDisplayedTapeSample();
        TraceSceneStateSummary("ApplySceneStateComplete", preserveLayout);
    }

    private void RestartGraphWarmup(int rotationSeed, bool preserveLayout)
    {
        if (_isValidationPaused)
        {
            TraceScene("RestartGraphWarmup skipped because validation pause is active.");
            return;
        }

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
        _lastGraphSelectionRefreshUtc = DateTimeOffset.UtcNow;
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
        int staleQuoteCount = _latestQuotes.Values.Count(quote => quote.IsStale);
        TraceSceneState(
            eventName,
            new KeyValuePair<string, object?>("preserve_layout", preserveLayout),
            new KeyValuePair<string, object?>("quote_count", _latestQuotes.Count),
            new KeyValuePair<string, object?>("stale_quote_count", staleQuoteCount),
            new KeyValuePair<string, object?>("graph_count", _graphs.Count),
            new KeyValuePair<string, object?>("tape_count", _tapes.Count),
            new KeyValuePair<string, object?>("updated_ticker_field", _statusViewModel?.UpdatedTickerFieldText),
            new KeyValuePair<string, object?>("data_freshness_text", _statusViewModel?.DataFreshnessText),
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
            .Where(symbol => _latestQuotes.TryGetValue(symbol, out QuoteSnapshot? quote) && quote.IsStale)
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
                if (_isValidationPaused)
                {
                    TraceScene("WarmGraphsAsync stopping because validation pause is active.");
                    break;
                }

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
        if (_isValidationPaused)
        {
            StopLiveTimers();
            TraceScene("ConfigureTimers skipped because validation pause is active.");
            return;
        }

        _clockTimer.Interval = TimeSpan.FromSeconds(Math.Max(1, _settings.ClockRefreshSeconds));
        _refreshTimer.Interval = RuntimeQuoteDispatchInterval;
        _backgroundTimer.Interval = TimeSpan.FromSeconds(Math.Max(5, _settings.BackgroundChangeSeconds));
        _worldDataTimer.Interval = TimeSpan.FromMinutes(10);
        _clockTimer.Start();
        _refreshTimer.Start();
        _worldDataTimer.Start();
        if (_backgroundPaths.Count > 1)
        {
            _backgroundTimer.Start();
            TraceSceneState(
                "BackgroundTimerArmed",
                new KeyValuePair<string, object?>("interval_seconds", _backgroundTimer.Interval.TotalSeconds),
                new KeyValuePair<string, object?>("background_count", _backgroundPaths.Count));
        }
        else
        {
            _backgroundTimer.Stop();
            TraceSceneState(
                "BackgroundTimerNotArmed",
                new KeyValuePair<string, object?>("interval_seconds", _backgroundTimer.Interval.TotalSeconds),
                new KeyValuePair<string, object?>("background_count", _backgroundPaths.Count));
        }
        _lastMotionTick = DateTime.UtcNow;
        _motionTimer.Start();
        TraceSceneState(
            "TimersConfigured",
            new KeyValuePair<string, object?>("clock_seconds", _clockTimer.Interval.TotalSeconds),
            new KeyValuePair<string, object?>("refresh_seconds", _refreshTimer.Interval.TotalSeconds),
            new KeyValuePair<string, object?>("background_seconds", _backgroundTimer.Interval.TotalSeconds),
            new KeyValuePair<string, object?>("world_data_minutes", _worldDataTimer.Interval.TotalMinutes),
            new KeyValuePair<string, object?>("background_rotation_enabled", _backgroundPaths.Count > 1));
    }

    private void InitializeRuntimeQuoteLoop()
    {
        RefreshOrderedRuntimeSymbols();
        TraceSceneState(
            "RuntimeQuoteLoopInitialized",
            new KeyValuePair<string, object?>("symbol_count", _orderedRuntimeSymbols.Count),
            new KeyValuePair<string, object?>("symbols", _orderedRuntimeSymbols.Take(12).ToList()));
    }

    private void StopRuntimeQuoteLoop()
    {
        _inFlightQuoteRequests.CancelAndClear();
    }

    private void StartMacroLane()
    {
        CancelMacroLane();
        CancellationTokenSource cancellation = new();
        _macroLaneCancellation = cancellation;
        _macroLaneTask = RunMacroLaneAsync(cancellation.Token);
        QueueMacroRefresh("startup");
    }

    private void CancelMacroLane()
    {
        if (_macroLaneCancellation is null)
            return;

        try
        {
            _macroLaneCancellation.Cancel();
        }
        catch
        {
        }

        _macroLaneCancellation.Dispose();
        _macroLaneCancellation = null;
        _macroLaneTask = null;
        System.Threading.Interlocked.Exchange(ref _macroLaneDirty, 0);
    }

    private void QueueMacroRefresh(string reason)
    {
        bool shouldSignal = System.Threading.Interlocked.Exchange(ref _macroLaneDirty, 1) == 0;
        TraceSceneState("MacroLaneRefreshQueued", new KeyValuePair<string, object?>("reason", reason));
        if (!shouldSignal)
            return;

        try
        {
            _macroLaneSignal.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }

    private async Task RunMacroLaneAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await _macroLaneSignal.WaitAsync(cancellationToken);
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (System.Threading.Volatile.Read(ref _macroLaneDirty) == 0)
                        break;

                    TimeSpan remaining = MacroLaneMinimumRefreshInterval - (DateTimeOffset.UtcNow - _lastMacroMeterRefreshUtc);
                    if (remaining > TimeSpan.Zero)
                        await Task.Delay(remaining, cancellationToken);

                    if (System.Threading.Interlocked.Exchange(ref _macroLaneDirty, 0) == 0)
                        continue;

                    MacroLaneSnapshot snapshot = await BuildMacroLaneSnapshotAsync(cancellationToken);
                    await Dispatcher.InvokeAsync(() =>
                    {
                        ApplyMacroLaneSnapshot(snapshot);
                        TraceSceneState(
                            "MacroUiPatchComplete",
                            new KeyValuePair<string, object?>("meter_count", snapshot.Meters.Count),
                            new KeyValuePair<string, object?>("missing_count", snapshot.MissingCount));
                    }, DispatcherPriority.Background, cancellationToken);
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            TraceScene($"RunMacroLaneAsync failed: {ex}");
        }
    }

    private void StartWorldMarketsLane()
    {
        CancelWorldMarketsLane();
        CancellationTokenSource cancellation = new();
        _worldMarketsLaneCancellation = cancellation;
        _worldMarketsLaneTask = RunWorldMarketsLaneAsync(cancellation.Token);
        QueueWorldMarketsRefresh(refreshAncillary: true, reason: "startup");
    }

    private void CancelWorldMarketsLane()
    {
        if (_worldMarketsLaneCancellation is null)
            return;

        try
        {
            _worldMarketsLaneCancellation.Cancel();
        }
        catch
        {
        }

        _worldMarketsLaneCancellation.Dispose();
        _worldMarketsLaneCancellation = null;
        _worldMarketsLaneTask = null;
        System.Threading.Interlocked.Exchange(ref _worldMarketsQuoteDirty, 0);
        System.Threading.Interlocked.Exchange(ref _worldMarketsAncillaryDirty, 0);
    }

    private void QueueWorldMarketsRefresh(bool refreshAncillary, string reason)
    {
        bool quoteWasDirty = System.Threading.Interlocked.Exchange(ref _worldMarketsQuoteDirty, 1) != 0;
        bool ancillaryWasDirty = refreshAncillary
            ? System.Threading.Interlocked.Exchange(ref _worldMarketsAncillaryDirty, 1) != 0
            : System.Threading.Volatile.Read(ref _worldMarketsAncillaryDirty) != 0;

        TraceSceneState(
            "WorldMarketsRefreshQueued",
            new KeyValuePair<string, object?>("reason", reason),
            new KeyValuePair<string, object?>("refresh_ancillary", refreshAncillary));
        if (quoteWasDirty || ancillaryWasDirty)
            return;

        try
        {
            _worldMarketsLaneSignal.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }

    private async Task RunWorldMarketsLaneAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await _worldMarketsLaneSignal.WaitAsync(cancellationToken);
                while (!cancellationToken.IsCancellationRequested)
                {
                    bool ancillaryPending = System.Threading.Volatile.Read(ref _worldMarketsAncillaryDirty) != 0;
                    bool quotePending = System.Threading.Volatile.Read(ref _worldMarketsQuoteDirty) != 0;
                    if (!ancillaryPending && !quotePending)
                        break;

                    if (!ancillaryPending)
                    {
                        TimeSpan remaining = WorldMarketsLaneMinimumRefreshInterval - (DateTimeOffset.UtcNow - _lastWorldMarketsLaneRefreshUtc);
                        if (remaining > TimeSpan.Zero)
                            await Task.Delay(remaining, cancellationToken);
                    }

                    bool refreshAncillary = System.Threading.Interlocked.Exchange(ref _worldMarketsAncillaryDirty, 0) != 0;
                    if (System.Threading.Interlocked.Exchange(ref _worldMarketsQuoteDirty, 0) == 0 && !refreshAncillary)
                        continue;

                    WorldMarketsLaneSnapshot snapshot = await BuildWorldMarketsLaneSnapshotAsync(refreshAncillary, cancellationToken);
                    await Dispatcher.InvokeAsync(() =>
                    {
                        ApplyWorldMarketsLaneSnapshot(snapshot);
                        TraceSceneState(
                            "WorldMarketsUiPatchComplete",
                            new KeyValuePair<string, object?>("city_count", snapshot.Cities.Count),
                            new KeyValuePair<string, object?>("market_status_text", snapshot.PinnedStatusText),
                            new KeyValuePair<string, object?>("weather_snapshot_count", snapshot.WeatherSnapshotCount),
                            new KeyValuePair<string, object?>("calendar_count", snapshot.ExchangeCalendarCount));
                    }, DispatcherPriority.Background, cancellationToken);
                    _lastWorldMarketsLaneRefreshUtc = DateTimeOffset.UtcNow;
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            TraceScene($"RunWorldMarketsLaneAsync failed: {ex}");
        }
    }

    private void StartNewsRefreshLoop()
    {
        CancelNewsRefreshLoop();
        CancellationTokenSource cancellation = new();
        _newsRefreshCancellation = cancellation;
        _newsRefreshTask = RunNewsRefreshLoopAsync(cancellation.Token);
    }

    private void CancelNewsRefreshLoop()
    {
        if (_newsRefreshCancellation is null)
            return;

        try
        {
            _newsRefreshCancellation.Cancel();
        }
        catch
        {
        }

        _newsRefreshCancellation.Dispose();
        _newsRefreshCancellation = null;
        _newsRefreshTask = null;
    }

    private async Task RunNewsRefreshLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RefreshNewsLaneAsync(force: true, cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(GetNewsRefreshPollInterval(), cancellationToken);
                await RefreshNewsLaneAsync(force: false, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            TraceScene($"RunNewsRefreshLoopAsync failed: {ex}");
        }
    }

    private async Task RefreshNewsLaneAsync(bool force, CancellationToken cancellationToken)
    {
        if (_isValidationPaused && !force)
            return;

        if (System.Threading.Interlocked.Exchange(ref _newsRefreshInFlight, 1) != 0)
            return;

        try
        {
            AppSettings settingsSnapshot = await Dispatcher.InvokeAsync(
                () => CloneNewsSettings(_settings),
                DispatcherPriority.Background,
                cancellationToken);
            bool networkAvailable = _networkAvailabilityService.IsNetworkAvailable();

            TraceSceneState(
                "NewsRefreshStart",
                new KeyValuePair<string, object?>("mode", settingsSnapshot.NewsScrollerMode),
                new KeyValuePair<string, object?>("force", force),
                new KeyValuePair<string, object?>("network_available", networkAvailable),
                new KeyValuePair<string, object?>("poll_seconds", GetNewsRefreshPollInterval().TotalSeconds));

            NewsFlasherViewModel refreshedNews = await _startupCoordinator.BuildNewsViewModelAsync(
                settingsSnapshot,
                networkAvailable,
                cancellationToken);

            await Dispatcher.InvokeAsync(() =>
            {
                bool changed = NewsChanged(refreshedNews);
                if (changed)
                    SyncNews(refreshedNews);

                TraceSceneState(
                    "NewsUiPatchComplete",
                    new KeyValuePair<string, object?>("mode", settingsSnapshot.NewsScrollerMode),
                    new KeyValuePair<string, object?>("changed", changed),
                    new KeyValuePair<string, object?>("headline_count", refreshedNews.Headlines.Count));
            }, DispatcherPriority.Background, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            TraceScene($"RefreshNewsLaneAsync failed: {ex}");
        }
        finally
        {
            System.Threading.Interlocked.Exchange(ref _newsRefreshInFlight, 0);
        }
    }

    private TimeSpan GetNewsRefreshPollInterval()
    {
        TimeSpan refreshInterval = FinanceNewsService.GetRefreshInterval(_settings);
        double pollSeconds = Math.Clamp(refreshInterval.TotalSeconds / 4d, 15d, 60d);
        return TimeSpan.FromSeconds(pollSeconds);
    }

    private void RefreshOrderedRuntimeSymbols()
    {
        _orderedRuntimeSymbols = _startupCoordinator.BuildOrderedRuntimeSymbols(_settings).ToList();
        if (_runtimeSymbolCursor >= _orderedRuntimeSymbols.Count)
            _runtimeSymbolCursor = 0;
    }

    private void DispatchNextRuntimeQuoteRequest()
    {
        if (_isValidationPaused || _orderedRuntimeSymbols.Count == 0)
            return;

        TraceRuntimeQuoteLoopHeartbeatIfDue();
        PruneStaleRuntimeQuoteRequests();
        RefreshGraphSelectionIfDue();

        // Keep the scene cadence strictly surgical: one lookup may be outstanding,
        // so slow transport cannot accumulate a burst of responses for the UI.
        if (_inFlightQuoteRequests.Count > 0)
        {
            TraceRuntimeQuoteDispatchSkippedIfDue("waiting_for_in_flight_request");
            return;
        }

        string? symbol = TakeNextRuntimeQuoteSymbol();
        if (string.IsNullOrWhiteSpace(symbol) && _inFlightQuoteRequests.Count > 0)
            TraceRuntimeQuoteDispatchSkippedIfDue("all_symbols_in_flight");

        if (string.IsNullOrWhiteSpace(symbol))
            return;

        CancellationTokenSource requestCancellation = new(RuntimeQuoteRequestTimeout);
        Task<IReadOnlyList<QuoteSnapshot>> requestTask = _runtimeQuoteProvider.GetQuotesAsync([symbol], requestCancellation.Token);
        _inFlightQuoteRequests.Add(symbol, requestTask, DateTimeOffset.UtcNow, requestCancellation);
        TraceSceneState(
            "RuntimeQuoteRequestQueued",
            new KeyValuePair<string, object?>("symbol", symbol),
            new KeyValuePair<string, object?>("timeout_seconds", RuntimeQuoteRequestTimeout.TotalSeconds),
            new KeyValuePair<string, object?>("in_flight_count", _inFlightQuoteRequests.Count));

        _ = requestTask.ContinueWith(
            task => Dispatcher.InvokeAsync(() => ApplyCompletedRuntimeQuote(symbol, task)),
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);
    }

    private void TraceRuntimeQuoteDispatchSkippedIfDue(string reason)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (now - _lastAllRuntimeQuotesInFlightTraceUtc < RuntimeQuoteRequestTimeout)
            return;

        _lastAllRuntimeQuotesInFlightTraceUtc = now;
        TraceSceneState(
            "RuntimeQuoteDispatchSkipped",
            new KeyValuePair<string, object?>("reason", reason),
            new KeyValuePair<string, object?>("in_flight_count", _inFlightQuoteRequests.Count));
    }

    private void PruneStaleRuntimeQuoteRequests()
    {
        if (_inFlightQuoteRequests.Count == 0)
            return;

        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (RuntimeQuoteTimedOutRequest<IReadOnlyList<QuoteSnapshot>> timedOut in _inFlightQuoteRequests.PruneStale(now, RuntimeQuoteRequestTimeout))
        {
            int failureStreak = IncrementRuntimeQuoteFailureStreak();
            UpdateStatusFreshnessText();
            ResetRuntimeQuoteTransportIfNeeded("timeout");
            TraceSceneState(
                "RuntimeQuoteRequestTimedOut",
                new KeyValuePair<string, object?>("symbol", timedOut.Symbol),
                new KeyValuePair<string, object?>("age_seconds", Math.Round(timedOut.Age.TotalSeconds, 1)),
                new KeyValuePair<string, object?>("failure_streak", failureStreak),
                new KeyValuePair<string, object?>("data_freshness_text", _statusViewModel?.DataFreshnessText),
                new KeyValuePair<string, object?>("in_flight_count", _inFlightQuoteRequests.Count));
        }
    }

    private void DispatchNextRuntimeQuoteRequestSafe()
    {
        try
        {
            DispatchNextRuntimeQuoteRequest();
        }
        catch (Exception ex)
        {
            ResetRuntimeQuoteTransportIfNeeded("dispatch-exception");
            TraceSceneState(
                "RuntimeQuoteDispatchFailed",
                new KeyValuePair<string, object?>("message", ex.Message),
                new KeyValuePair<string, object?>("failure_streak", ReadRuntimeQuoteFailureStreak()),
                new KeyValuePair<string, object?>("in_flight_count", _inFlightQuoteRequests.Count));
        }
    }

    private void TraceRuntimeQuoteLoopHeartbeatIfDue()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (now - _lastRuntimeQuoteLoopHeartbeatUtc < TimeSpan.FromSeconds(30))
            return;

        _lastRuntimeQuoteLoopHeartbeatUtc = now;
        TraceSceneState(
            "RuntimeQuoteLoopHeartbeat",
            new KeyValuePair<string, object?>("failure_streak", ReadRuntimeQuoteFailureStreak()),
            new KeyValuePair<string, object?>("in_flight_count", _inFlightQuoteRequests.Count),
            new KeyValuePair<string, object?>("symbol_count", _orderedRuntimeSymbols.Count),
            new KeyValuePair<string, object?>("data_freshness_text", _statusViewModel?.DataFreshnessText));
    }

    private void ResetRuntimeQuoteTransportIfNeeded(string reason)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        int failureStreak = ReadRuntimeQuoteFailureStreak();
        if (!_runtimeQuoteRecoveryGate.TryEnter(failureStreak, now))
            return;

        try
        {
            YFinanceRuntimeClientFactory.ResetConnectionStateForRecovery($"runtime-quote-{reason}");
            _runtimeQuoteRecoveryGate.MarkResetSucceeded(now);
            TraceSceneState(
                "RuntimeQuoteTransportReset",
                new KeyValuePair<string, object?>("reason", reason),
                new KeyValuePair<string, object?>("failure_streak", failureStreak),
                new KeyValuePair<string, object?>("in_flight_count", _inFlightQuoteRequests.Count));
        }
        catch (Exception ex)
        {
            TraceSceneState(
                "RuntimeQuoteTransportResetFailed",
                new KeyValuePair<string, object?>("reason", reason),
                new KeyValuePair<string, object?>("message", ex.Message),
                new KeyValuePair<string, object?>("failure_streak", failureStreak),
                new KeyValuePair<string, object?>("in_flight_count", _inFlightQuoteRequests.Count));
        }
        finally
        {
            _runtimeQuoteRecoveryGate.Exit();
        }
    }

    private int ReadRuntimeQuoteFailureStreak()
        => Volatile.Read(ref _runtimeQuoteFailureStreak);

    private int IncrementRuntimeQuoteFailureStreak()
        => Interlocked.Increment(ref _runtimeQuoteFailureStreak);

    private void ResetRuntimeQuoteFailureStreak()
        => Interlocked.Exchange(ref _runtimeQuoteFailureStreak, 0);

    private string? TakeNextRuntimeQuoteSymbol()
    {
        if (_orderedRuntimeSymbols.Count == 0)
            return null;

        int scanned = 0;
        while (scanned < _orderedRuntimeSymbols.Count)
        {
            if (_runtimeSymbolCursor >= _orderedRuntimeSymbols.Count)
                _runtimeSymbolCursor = 0;

            string symbol = _orderedRuntimeSymbols[_runtimeSymbolCursor];
            _runtimeSymbolCursor = (_runtimeSymbolCursor + 1) % _orderedRuntimeSymbols.Count;
            scanned++;

            if (_inFlightQuoteRequests.Contains(symbol))
                continue;

            return symbol;
        }

        return null;
    }

    public void SetValidationPause(bool paused)
    {
        if (_isValidationPaused == paused)
            return;

        _isValidationPaused = paused;
        if (paused)
        {
            StopLiveTimers();
            CancelGraphWarmup();
            TraceScene("Scene paused for config session.");
            return;
        }

        ConfigureTimers();
        if (_initialized)
            _ = RefreshSceneAfterValidationPauseAsync();

        TraceScene("Scene resumed after config session.");
    }

    private void StopLiveTimers()
    {
        StopRuntimeQuoteLoop();
        _clockTimer.Stop();
        _refreshTimer.Stop();
        _backgroundTimer.Stop();
        _backgroundZoomTimer.Stop();
        CancelBackgroundRecoveryReload();
        _worldDataTimer.Stop();
        _motionTimer.Stop();
    }

    private async Task RefreshSceneAfterValidationPauseAsync()
    {
        try
        {
            await RefreshSceneAsync(preserveLayout: false, fullAncillaryRefresh: true);
            InitializeRuntimeQuoteLoop();
            _ = RefreshNewsLaneAsync(force: true, CancellationToken.None);
        }
        catch (Exception ex)
        {
            TraceScene($"RefreshSceneAfterValidationPauseAsync failed: {ex}");
        }
    }

    private void RefreshGraphSelectionIfDue()
    {
        if (!_settings.EnableFloatingGraphs)
            return;

        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        if (_lastGraphSelectionRefreshUtc != DateTimeOffset.MinValue &&
            nowUtc - _lastGraphSelectionRefreshUtc < GraphSelectionRefreshInterval)
        {
            return;
        }

        TraceSceneState(
            "GraphSelectionRefreshDue",
            new KeyValuePair<string, object?>("last_refresh_utc", _lastGraphSelectionRefreshUtc == DateTimeOffset.MinValue ? null : _lastGraphSelectionRefreshUtc),
            new KeyValuePair<string, object?>("graph_count", _graphs.Count));
        RestartGraphWarmup(_graphRotationSeed, preserveLayout: false);
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
            UpdateStatusFreshnessText();
            if (refreshStatusAncillary)
                _lastStatusAncillaryRefreshUtc = referenceUtc;
        }
    }

    private void UpdateStatusMacroMeters(bool force)
    {
        const decimal treasuryYieldMeterMax = 6m;
        const decimal nasdaqMeterMax = 25000m;
        const decimal bitcoinMeterMax = 200000m;

        if (_statusViewModel is null)
            return;

        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        if (!force && nowUtc - _lastMacroMeterRefreshUtc < TimeSpan.FromSeconds(8))
            return;

        EnsureMacroMetersInitialized();
        UpdateQuoteMeter(_statusViewModel.MacroMeters[0], "VIX", "^VIX", 60m, invertRiskColors: true);
        UpdateQuoteMeter(_statusViewModel.MacroMeters[1], "NASDAQ", "^IXIC", nasdaqMeterMax);
        UpdateQuoteMeter(_statusViewModel.MacroMeters[2], "UST10Y", "^TNX", treasuryYieldMeterMax);
        UpdateQuoteMeter(_statusViewModel.MacroMeters[3], "UST3M", "^IRX", treasuryYieldMeterMax);
        UpdateQuoteMeter(_statusViewModel.MacroMeters[4], "GOLD", "GC=F", 4000m);
        UpdateQuoteMeter(_statusViewModel.MacroMeters[5], "CRUDE", "BZ=F", 160m);
        UpdateQuoteMeter(_statusViewModel.MacroMeters[6], "DXY", "DX-Y.NYB", 120m, invertRiskColors: true);
        UpdateQuoteMeter(_statusViewModel.MacroMeters[7], "BTC", "BTC-USD", bitcoinMeterMax);

        _lastMacroMeterRefreshUtc = nowUtc;
        TraceMacroSnapshot(force);
    }

    private void EnsureMacroMetersInitialized()
    {
        if (_statusViewModel is null)
            return;

        string[] labels = ["VIX", "NASDAQ", "UST10Y", "UST3M", "GOLD", "CRUDE", "DXY", "BTC"];
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
                    ChangeText = string.Empty
                });
                continue;
            }

            _statusViewModel.MacroMeters[index].Label = labels[index];
        }
    }

    private void UpdateQuoteMeter(
        MacroMeterViewModel meter,
        string label,
        string symbol,
        decimal maxValue,
        bool invertRiskColors = false)
    {
        meter.Label = label;
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
        bool isStale = quote.IsStale;
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

    private async Task RefreshClockDataAsync(bool force)
    {
        await Task.Yield();
        QueueWorldMarketsRefresh(refreshAncillary: force, reason: force ? "clock-data-force" : "clock-data");
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
            if (refreshAncillary || string.IsNullOrWhiteSpace(city.ZoneText))
            {
                city.ZoneText = BuildClockFooterWithMarketStatus(city, zone, cityTime, referenceUtc);
                ApplyClockCardTheme(city, cityTime);
            }
        }
    }

    private void ApplyClockMarketData(bool force)
    {
        Dispatcher.VerifyAccess();

        if (_clockViewModel is null)
            return;

        List<string> missingSymbols = [];
        int populatedCount = 0;
        DateTimeOffset referenceUtc = GetReferenceUtcNow();
        foreach (ClockCityViewModel city in _clockViewModel.Cities.Where(city => city.ShowExchangeDetails))
        {
            if (string.IsNullOrWhiteSpace(city.ExchangeSymbol))
                continue;

            if (!_latestQuotes.TryGetValue(city.ExchangeSymbol, out QuoteSnapshot? quote))
            {
                ApplyExchangeCardMarketStatus(city, null, referenceUtc);
                missingSymbols.Add(city.ExchangeSymbol);
                continue;
            }

            ApplyExchangeCardMarketStatus(city, quote, referenceUtc);

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
                    SetMiniGraphPointsIfChanged(city, history, 72d, 12d);
            }
        }

        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        if (nowUtc - _lastClockMarketTraceUtc >= TimeSpan.FromSeconds(30))
        {
            _lastClockMarketTraceUtc = nowUtc;
            TraceSceneState(
                "ClockMarketDataSummary",
                new KeyValuePair<string, object?>("populated_exchange_count", populatedCount),
                new KeyValuePair<string, object?>("loading_exchange_count", 0),
                new KeyValuePair<string, object?>("loading_exchange_symbols", Array.Empty<string>()),
                new KeyValuePair<string, object?>("missing_exchange_count", missingSymbols.Count),
                new KeyValuePair<string, object?>("missing_exchange_symbols", missingSymbols.Take(10).ToList()));
        }
    }

    private static void SetMiniGraphPointsIfChanged(ClockCityViewModel target, IReadOnlyList<decimal> values, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetMiniGraphPointsIfChanged(BuildMiniGraphPointList(values, width, height));
    }

    private static IReadOnlyList<Point> BuildMiniGraphPointList(IReadOnlyList<decimal> values, double width, double height)
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
        List<Point> points = new(values.Count);
        for (int index = 0; index < values.Count; index++)
        {
            decimal normalized = (values[index] - min) / range;
            double y = height - (double)normalized * height;
            points.Add(new Point(index * stepX, y));
        }

        return points;
    }

    private async Task<MacroLaneSnapshot> BuildMacroLaneSnapshotAsync(CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, QuoteSnapshot> quotes = await Dispatcher.InvokeAsync(
            () => SnapshotQuotes(_latestQuotes),
            DispatcherPriority.Background,
            cancellationToken);

        TraceSceneState(
            "MacroRefreshStart",
            new KeyValuePair<string, object?>("quote_count", quotes.Count));

        List<MacroMeterState> meters =
        [
            BuildMacroMeterState("VIX", "^VIX", 60m, quotes, invertRiskColors: true),
            BuildMacroMeterState("NASDAQ", "^IXIC", 25000m, quotes),
            BuildMacroMeterState("UST10Y", "^TNX", 6m, quotes),
            BuildMacroMeterState("UST3M", "^IRX", 6m, quotes),
            BuildMacroMeterState("GOLD", "GC=F", 4000m, quotes),
            BuildMacroMeterState("CRUDE", "BZ=F", 160m, quotes),
            BuildMacroMeterState("DXY", "DX-Y.NYB", 120m, quotes, invertRiskColors: true),
            BuildMacroMeterState("BTC", "BTC-USD", 200000m, quotes)
        ];

        TraceSceneState(
            "MacroRefreshPrepared",
            new KeyValuePair<string, object?>("meter_count", meters.Count),
            new KeyValuePair<string, object?>("missing_count", meters.Count(meter => meter.IsMissing)));

        return new MacroLaneSnapshot(meters, meters.Count(meter => meter.IsMissing));
    }

    private void ApplyMacroLaneSnapshot(MacroLaneSnapshot snapshot)
    {
        if (_isValidationPaused || _statusViewModel is null)
            return;

        EnsureMacroMetersInitialized();
        for (int index = 0; index < snapshot.Meters.Count && index < _statusViewModel.MacroMeters.Count; index++)
        {
            MacroMeterViewModel target = _statusViewModel.MacroMeters[index];
            MacroMeterState source = snapshot.Meters[index];
            target.Label = source.Label;
            target.ValueText = source.ValueText;
            target.ChangeText = source.ChangeText;
            target.AccentBrush = source.AccentBrush;
            target.SetFill(source.Fill);
        }

        _lastMacroMeterRefreshUtc = DateTimeOffset.UtcNow;
        TraceMacroSnapshot(force: true);
    }

    private static MacroMeterState BuildMacroMeterState(
        string label,
        string symbol,
        decimal maxValue,
        IReadOnlyDictionary<string, QuoteSnapshot> quotes,
        bool invertRiskColors = false)
    {
        string valueText = "--";
        string changeText = string.Empty;
        Brush accentBrush = Brushes.SlateGray;
        double fill = 0d;
        bool isMissing = true;

        if (quotes.TryGetValue(symbol, out QuoteSnapshot? quote))
        {
            decimal? last = quote.Last ?? quote.PreviousClose;
            decimal? changePercent = quote.ChangePercent;
            if (last is decimal lastValue)
            {
                valueText = lastValue.ToString("0.00");
                changeText = changePercent is decimal percent
                    ? $"{(percent >= 0 ? "+" : string.Empty)}{percent:0.0}%"
                    : string.Empty;
                Brush upBrush = invertRiskColors ? Brushes.OrangeRed : Brushes.LimeGreen;
                Brush downBrush = invertRiskColors ? Brushes.LimeGreen : Brushes.OrangeRed;
                accentBrush = quote.IsStale
                    ? Brushes.Goldenrod
                    : changePercent switch
                    {
                        > 0m => upBrush,
                        < 0m => downBrush,
                        _ => Brushes.Gainsboro
                    };
                fill = (double)Math.Clamp(lastValue / Math.Max(1m, maxValue), 0m, 1m);
                isMissing = false;
            }
        }

        return new MacroMeterState(label, valueText, changeText, accentBrush, fill, isMissing);
    }

    private async Task<WorldMarketsLaneSnapshot> BuildWorldMarketsLaneSnapshotAsync(bool refreshAncillary, CancellationToken cancellationToken)
    {
        WorldMarketsInputSnapshot input = await Dispatcher.InvokeAsync(
            SnapshotWorldMarketsInput,
            DispatcherPriority.Background,
            cancellationToken);
        if (input.ClockTitle is null)
            return WorldMarketsLaneSnapshot.Empty;

        bool networkAvailable = _networkAvailabilityService.IsNetworkAvailable();
        TraceSceneState(
            "WorldMarketsRefreshStart",
            new KeyValuePair<string, object?>("refresh_ancillary", refreshAncillary),
            new KeyValuePair<string, object?>("network_available", networkAvailable),
            new KeyValuePair<string, object?>("city_count", input.Cities.Count));

        Dictionary<string, WeatherSnapshot> weatherSnapshots = input.WeatherSnapshots;
        ExchangeCalendarSet calendarSet = input.ExchangeCalendars;
        TimeSpan? ntpOffset = input.NtpOffset;
        DateTimeOffset lastNtpSyncUtc = input.LastNtpSyncUtc;
        DateTimeOffset lastWeatherRefreshUtc = input.LastWeatherRefreshUtc;
        DateTimeOffset lastMarketCalendarRefreshUtc = input.LastMarketCalendarRefreshUtc;
        string subtitle = input.ClockSubtitle;

        DateTimeOffset utcNow = DateTimeOffset.UtcNow;
        bool refreshedNtp = false;
        bool refreshedWeather = false;
        bool refreshedCalendars = false;

        if (refreshAncillary)
        {
            bool shouldRefreshNtp = utcNow - lastNtpSyncUtc >= TimeSpan.FromMinutes(10);
            if (shouldRefreshNtp)
            {
                refreshedNtp = true;
                if (networkAvailable)
                {
                    NtpSyncResult syncResult = await _ntpTimeService.TryGetUtcNowAsync();
                    ntpOffset = syncResult.Success ? syncResult.UtcNow - DateTimeOffset.UtcNow : null;
                }
                else
                {
                    ntpOffset = null;
                }

                lastNtpSyncUtc = utcNow;
                subtitle = string.Empty;
            }

            bool shouldRefreshWeather = utcNow - lastWeatherRefreshUtc >= TimeSpan.FromMinutes(10);
            if (shouldRefreshWeather)
            {
                refreshedWeather = true;
                weatherSnapshots = (await _worldWeatherService.GetWeatherAsync(
                    input.Cities.Select(CloneCityForWeatherService),
                    networkAvailable,
                    cancellationToken)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
                lastWeatherRefreshUtc = utcNow;
            }

            bool shouldRefreshCalendars = utcNow - lastMarketCalendarRefreshUtc >= TimeSpan.FromHours(Math.Max(1, input.MarketCalendarRefreshHours));
            if (shouldRefreshCalendars)
            {
                refreshedCalendars = true;
                calendarSet = await _exchangeMarketCalendarService.GetCalendarSetAsync(
                    input.Cities
                        .Where(city => city.ShowExchangeDetails && !string.IsNullOrWhiteSpace(city.CalendarExchangeCode))
                        .Select(city => new ExchangeCalendarRequest
                        {
                            CityKey = city.Key,
                            ExchangeCode = city.CalendarExchangeCode,
                            ExchangeName = city.ExchangeName,
                            ExchangeSymbol = city.ExchangeSymbol,
                            TimeZoneId = city.PrimaryTimeZoneId,
                            AlternateTimeZoneId = city.SecondaryTimeZoneId
                        })
                        .ToList(),
                    networkAvailable);
                lastMarketCalendarRefreshUtc = utcNow;
            }
        }

        DateTimeOffset referenceUtc = ntpOffset.HasValue && DateTimeOffset.UtcNow - lastNtpSyncUtc <= TimeSpan.FromMinutes(20)
            ? DateTimeOffset.UtcNow + ntpOffset.Value
            : DateTimeOffset.UtcNow;

        Dictionary<string, List<decimal>> updatedHistory = input.ClockIndexHistory.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToList(),
            StringComparer.OrdinalIgnoreCase);
        List<WorldMarketCityState> cityStates = [];
        List<string> missingSymbols = [];
        int populatedCount = 0;

        foreach (ClockCityViewModel city in input.Cities)
        {
            ClockCityViewModel working = CloneClockCity(city);
            TimeZoneInfo zone = string.Equals(working.Key, "Local", StringComparison.OrdinalIgnoreCase)
                ? TimeZoneInfo.Local
                : ResolveTimeZone(working.PrimaryTimeZoneId, working.SecondaryTimeZoneId);
            DateTimeOffset cityTime = TimeZoneInfo.ConvertTime(referenceUtc, zone);
            working.TimeText = FormatClockTimeWithZone(cityTime, zone);
            working.ZoneText = BuildClockFooterWithMarketStatus(working, zone, cityTime, referenceUtc, calendarSet);
            ApplyClockCardTheme(working, cityTime);

            if (!working.SupportsWeather)
            {
                working.WeatherGlyph = string.Empty;
                working.WeatherText = "Clock only";
            }
            else if (weatherSnapshots.TryGetValue(working.Key, out WeatherSnapshot? snapshot))
            {
                working.WeatherGlyph = WorldWeatherService.GetGlyph(snapshot.WeatherCode, snapshot.IsDay);
                working.WeatherText = $"{Math.Round(snapshot.TemperatureCelsius):0}C";
            }
            else
            {
                working.WeatherGlyph = string.Empty;
                working.WeatherText = "Weather unavailable";
            }

            if (working.ShowExchangeDetails && !string.IsNullOrWhiteSpace(working.ExchangeSymbol))
            {
                input.Quotes.TryGetValue(working.ExchangeSymbol, out QuoteSnapshot? quote);
                ApplyExchangeCardMarketStatus(working, quote, referenceUtc, calendarSet);

                decimal? last = quote?.Last ?? quote?.PreviousClose;
                decimal? changePercent = quote?.ChangePercent;
                Brush changeBrush = changePercent switch
                {
                    > 0m => Brushes.LimeGreen,
                    < 0m => Brushes.OrangeRed,
                    _ => Brushes.Gainsboro
                };

                working.IndexValueText = last is decimal value ? value.ToString("0.00") : "--";
                working.IndexChangeText = changePercent is decimal percent
                    ? $"{(percent >= 0 ? "+" : string.Empty)}{percent:0.00}%"
                    : "--";
                working.IndexChangeForeground = changeBrush;
                working.MiniGraphStroke = changeBrush;

                if (last is decimal lastValue)
                {
                    populatedCount++;
                    if (!updatedHistory.TryGetValue(working.ExchangeSymbol, out List<decimal>? history))
                    {
                        history = [];
                        updatedHistory[working.ExchangeSymbol] = history;
                    }

                    bool shouldAppend = history.Count == 0 || Math.Abs(history[^1] - lastValue) > 0.0001m;
                    if (shouldAppend)
                    {
                        history.Add(lastValue);
                        while (history.Count > 24)
                            history.RemoveAt(0);
                    }

                    IReadOnlyList<Point> points = BuildMiniGraphPointList(history, 72d, 12d);
                    cityStates.Add(WorldMarketCityState.FromViewModel(working, points));
                    continue;
                }

                missingSymbols.Add(working.ExchangeSymbol);
            }

            cityStates.Add(WorldMarketCityState.FromViewModel(working, []));
        }

        string pinnedStatusText = BuildPinnedNewYorkStatusBandText(input.Cities, input.Quotes, calendarSet, referenceUtc);
        TraceSceneState(
            "WorldMarketsFetchComplete",
            new KeyValuePair<string, object?>("refresh_ancillary", refreshAncillary),
            new KeyValuePair<string, object?>("network_available", networkAvailable),
            new KeyValuePair<string, object?>("ntp_refreshed", refreshedNtp),
            new KeyValuePair<string, object?>("weather_refreshed", refreshedWeather),
            new KeyValuePair<string, object?>("calendar_refreshed", refreshedCalendars),
            new KeyValuePair<string, object?>("weather_snapshot_count", weatherSnapshots.Count),
            new KeyValuePair<string, object?>("calendar_count", calendarSet.CalendarsByCityKey.Count));
        TraceSceneState(
            "WorldMarketsMergeComplete",
            new KeyValuePair<string, object?>("populated_exchange_count", populatedCount),
            new KeyValuePair<string, object?>("missing_exchange_count", missingSymbols.Count),
            new KeyValuePair<string, object?>("missing_exchange_symbols", missingSymbols.Take(10).ToList()));

        return new WorldMarketsLaneSnapshot(
            input.ClockTitle,
            subtitle,
            cityStates,
            pinnedStatusText,
            weatherSnapshots,
            calendarSet,
            updatedHistory,
            ntpOffset,
            lastNtpSyncUtc,
            lastWeatherRefreshUtc,
            lastMarketCalendarRefreshUtc,
            weatherSnapshots.Count,
            calendarSet.CalendarsByCityKey.Count);
    }

    private void ApplyWorldMarketsLaneSnapshot(WorldMarketsLaneSnapshot snapshot)
    {
        Dispatcher.VerifyAccess();

        if (_isValidationPaused || _clockViewModel is null || _statusViewModel is null || snapshot.ClockTitle is null)
            return;

        _clockViewModel.Title = snapshot.ClockTitle;
        _clockViewModel.Subtitle = snapshot.ClockSubtitle;
        _statusViewModel.MarketStatusText = snapshot.PinnedStatusText;
        _weatherSnapshots = snapshot.WeatherSnapshots;
        _exchangeCalendars = snapshot.ExchangeCalendars;
        _ntpOffset = snapshot.NtpOffset;
        _lastNtpSyncUtc = snapshot.LastNtpSyncUtc;
        _lastWeatherRefreshUtc = snapshot.LastWeatherRefreshUtc;
        _lastMarketCalendarRefreshUtc = snapshot.LastMarketCalendarRefreshUtc;

        ReplaceClockIndexHistory(snapshot.ClockIndexHistory);

        foreach (WorldMarketCityState source in snapshot.Cities)
        {
            ClockCityViewModel? target = _clockViewModel.Cities.FirstOrDefault(city => string.Equals(city.Key, source.Key, StringComparison.OrdinalIgnoreCase));
            if (target is null)
                continue;

            target.TimeText = source.TimeText;
            target.ZoneText = source.ZoneText;
            target.WeatherGlyph = source.WeatherGlyph;
            target.WeatherText = source.WeatherText;
            target.MarketStatusText = source.MarketStatusText;
            target.MarketStatusForeground = source.MarketStatusForeground;
            target.IndexValueText = source.IndexValueText;
            target.IndexChangeText = source.IndexChangeText;
            target.IndexChangeForeground = source.IndexChangeForeground;
            target.MiniGraphStroke = source.MiniGraphStroke;
            target.CardBackground = source.CardBackground;
            target.CardBorderBrush = source.CardBorderBrush;
            target.SetMiniGraphPointsIfChanged(source.MiniGraphPoints);
        }
    }

    private static IReadOnlyDictionary<string, QuoteSnapshot> SnapshotQuotes(IReadOnlyDictionary<string, QuoteSnapshot> quotes)
        => quotes.ToDictionary(
            pair => pair.Key,
            pair => CloneQuoteSnapshot(pair.Value),
            StringComparer.OrdinalIgnoreCase);

    private WorldMarketsInputSnapshot SnapshotWorldMarketsInput()
    {
        if (_clockViewModel is null)
            return WorldMarketsInputSnapshot.Empty;

        return new WorldMarketsInputSnapshot(
            _clockViewModel.Title,
            _clockViewModel.Subtitle,
            _clockViewModel.Cities.Select(CloneClockCity).ToList(),
            SnapshotQuotes(_latestQuotes),
            CloneWeatherSnapshots(_weatherSnapshots),
            CloneExchangeCalendarSet(_exchangeCalendars),
            CloneClockIndexHistory(_clockIndexHistory),
            _ntpOffset,
            _lastNtpSyncUtc,
            _lastWeatherRefreshUtc,
            _lastMarketCalendarRefreshUtc,
            _settings.MarketCalendarRefreshHours);
    }

    private static QuoteSnapshot CloneQuoteSnapshot(QuoteSnapshot snapshot)
        => new()
        {
            Symbol = snapshot.Symbol,
            Last = snapshot.Last,
            Change = snapshot.Change,
            ChangePercent = snapshot.ChangePercent,
            PreviousClose = snapshot.PreviousClose,
            Currency = snapshot.Currency,
            MarketSession = snapshot.MarketSession,
            ProviderTimestampUtc = snapshot.ProviderTimestampUtc,
            FetchTimestampUtc = snapshot.FetchTimestampUtc,
            IsStale = snapshot.IsStale
        };

    private static Dictionary<string, WeatherSnapshot> CloneWeatherSnapshots(IReadOnlyDictionary<string, WeatherSnapshot> snapshots)
        => snapshots.ToDictionary(
            pair => pair.Key,
            pair => new WeatherSnapshot
            {
                CityKey = pair.Value.CityKey,
                TemperatureCelsius = pair.Value.TemperatureCelsius,
                WeatherCode = pair.Value.WeatherCode,
                IsDay = pair.Value.IsDay,
                FetchTimestampUtc = pair.Value.FetchTimestampUtc
            },
            StringComparer.OrdinalIgnoreCase);

    private static ExchangeCalendarSet CloneExchangeCalendarSet(ExchangeCalendarSet source)
    {
        ExchangeCalendarSet clone = new()
        {
            GeneratedUtc = source.GeneratedUtc,
            Source = source.Source
        };
        clone.Overlay(source);
        return clone;
    }

    private static Dictionary<string, List<decimal>> CloneClockIndexHistory(IReadOnlyDictionary<string, List<decimal>> source)
        => source.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToList(),
            StringComparer.OrdinalIgnoreCase);

    private void ReplaceClockIndexHistory(IReadOnlyDictionary<string, List<decimal>> replacement)
    {
        _clockIndexHistory.Clear();
        foreach ((string key, List<decimal> values) in replacement)
            _clockIndexHistory[key] = values.ToList();
    }

    private static ClockCityViewModel CloneClockCity(ClockCityViewModel source)
        => new()
        {
            Key = source.Key,
            Label = source.Label,
            PrimaryTimeZoneId = source.PrimaryTimeZoneId,
            SecondaryTimeZoneId = source.SecondaryTimeZoneId,
            TimeText = source.TimeText,
            ZoneText = source.ZoneText,
            WeatherGlyph = source.WeatherGlyph,
            WeatherText = source.WeatherText,
            FlagGlyph = source.FlagGlyph,
            FlagCode = source.FlagCode,
            SupportsWeather = source.SupportsWeather,
            Latitude = source.Latitude,
            Longitude = source.Longitude,
            IsLocalSummary = source.IsLocalSummary,
            ShowExchangeDetails = source.ShowExchangeDetails,
            ExchangeName = source.ExchangeName,
            ExchangeSymbol = source.ExchangeSymbol,
            CalendarExchangeCode = source.CalendarExchangeCode,
            MarketStatusText = source.MarketStatusText,
            MarketStatusForeground = source.MarketStatusForeground,
            IndexValueText = source.IndexValueText,
            IndexChangeText = source.IndexChangeText,
            IndexChangeForeground = source.IndexChangeForeground,
            MiniGraphStroke = source.MiniGraphStroke,
            MiniGraphPoints = new PointCollection(source.MiniGraphPoints),
            CardBackground = source.CardBackground,
            CardBorderBrush = source.CardBorderBrush
        };

    private static ClockCityViewModel CloneCityForWeatherService(ClockCityViewModel source)
        => new()
        {
            Key = source.Key,
            Label = source.Label,
            SupportsWeather = source.SupportsWeather,
            Latitude = source.Latitude,
            Longitude = source.Longitude
        };

    private sealed record MacroMeterState(
        string Label,
        string ValueText,
        string ChangeText,
        Brush AccentBrush,
        double Fill,
        bool IsMissing);

    private sealed record MacroLaneSnapshot(
        IReadOnlyList<MacroMeterState> Meters,
        int MissingCount);

    private sealed record WorldMarketCityState(
        string Key,
        string TimeText,
        string ZoneText,
        string WeatherGlyph,
        string WeatherText,
        string MarketStatusText,
        Brush MarketStatusForeground,
        string IndexValueText,
        string IndexChangeText,
        Brush IndexChangeForeground,
        Brush MiniGraphStroke,
        Brush CardBackground,
        Brush CardBorderBrush,
        IReadOnlyList<Point> MiniGraphPoints)
    {
        public static WorldMarketCityState FromViewModel(ClockCityViewModel source, IReadOnlyList<Point> miniGraphPoints)
            => new(
                source.Key,
                source.TimeText,
                source.ZoneText,
                source.WeatherGlyph,
                source.WeatherText,
                source.MarketStatusText,
                source.MarketStatusForeground,
                source.IndexValueText,
                source.IndexChangeText,
                source.IndexChangeForeground,
                source.MiniGraphStroke,
                source.CardBackground,
                source.CardBorderBrush,
                miniGraphPoints);
    }

    private sealed record WorldMarketsInputSnapshot(
        string? ClockTitle,
        string ClockSubtitle,
        IReadOnlyList<ClockCityViewModel> Cities,
        IReadOnlyDictionary<string, QuoteSnapshot> Quotes,
        Dictionary<string, WeatherSnapshot> WeatherSnapshots,
        ExchangeCalendarSet ExchangeCalendars,
        Dictionary<string, List<decimal>> ClockIndexHistory,
        TimeSpan? NtpOffset,
        DateTimeOffset LastNtpSyncUtc,
        DateTimeOffset LastWeatherRefreshUtc,
        DateTimeOffset LastMarketCalendarRefreshUtc,
        int MarketCalendarRefreshHours)
    {
        public static WorldMarketsInputSnapshot Empty { get; } = new(
            null,
            string.Empty,
            [],
            new Dictionary<string, QuoteSnapshot>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, WeatherSnapshot>(StringComparer.OrdinalIgnoreCase),
            new ExchangeCalendarSet(),
            new Dictionary<string, List<decimal>>(StringComparer.OrdinalIgnoreCase),
            null,
            DateTimeOffset.MinValue,
            DateTimeOffset.MinValue,
            DateTimeOffset.MinValue,
            1);
    }

    private sealed record WorldMarketsLaneSnapshot(
        string? ClockTitle,
        string ClockSubtitle,
        IReadOnlyList<WorldMarketCityState> Cities,
        string PinnedStatusText,
        Dictionary<string, WeatherSnapshot> WeatherSnapshots,
        ExchangeCalendarSet ExchangeCalendars,
        Dictionary<string, List<decimal>> ClockIndexHistory,
        TimeSpan? NtpOffset,
        DateTimeOffset LastNtpSyncUtc,
        DateTimeOffset LastWeatherRefreshUtc,
        DateTimeOffset LastMarketCalendarRefreshUtc,
        int WeatherSnapshotCount,
        int ExchangeCalendarCount)
    {
        public static WorldMarketsLaneSnapshot Empty { get; } = new(
            null,
            string.Empty,
            [],
            "Market (New York): --",
            new Dictionary<string, WeatherSnapshot>(StringComparer.OrdinalIgnoreCase),
            new ExchangeCalendarSet(),
            new Dictionary<string, List<decimal>>(StringComparer.OrdinalIgnoreCase),
            null,
            DateTimeOffset.MinValue,
            DateTimeOffset.MinValue,
            DateTimeOffset.MinValue,
            0,
            0);
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

    private static string FormatExchangeCardStatusText(QuoteSnapshot? quote, ExchangeCalendarStatus? fallbackStatus)
    {
        MarketSession effectiveSession = quote?.MarketSession switch
        {
            MarketSession.Unknown or null => fallbackStatus?.Session ?? MarketSession.Unknown,
            _ => quote.MarketSession
        };

        return effectiveSession switch
        {
            MarketSession.Regular => "OPEN",
            MarketSession.PreMarket => "PRE",
            MarketSession.AfterHours => "POST",
            MarketSession.Closed => "CLOSED",
            _ => fallbackStatus?.IsOpen == true ? "OPEN" : fallbackStatus is not null ? "CLOSED" : "--"
        };
    }

    private static string GetClockFooter(ClockCityViewModel city, TimeZoneInfo zone, DateTimeOffset pointInTime)
        => GetTimeZoneAbbreviation(zone, pointInTime);

    private static string FormatHoursAndMinutes(TimeSpan timeSpan)
    {
        if (timeSpan < TimeSpan.Zero)
            timeSpan = TimeSpan.Zero;

        int totalHours = (int)Math.Floor(timeSpan.TotalHours);
        return $"{totalHours:00}:{timeSpan.Minutes:00}";
    }

    private static string FormatDaysHoursAndMinutes(TimeSpan timeSpan)
    {
        if (timeSpan < TimeSpan.Zero)
            timeSpan = TimeSpan.Zero;

        int totalHours = (int)Math.Floor(timeSpan.TotalHours);
        int days = totalHours / 24;
        int hours = totalHours % 24;
        if (days <= 0)
            return $"{hours:00}h{timeSpan.Minutes:00}m";

        return $"{days:00}d{hours:00}h{timeSpan.Minutes:00}m";
    }

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

    private string BuildClockFooterWithMarketStatus(
        ClockCityViewModel city,
        TimeZoneInfo zone,
        DateTimeOffset cityTime,
        DateTimeOffset referenceUtc,
        ExchangeCalendarSet calendarSet)
    {
        string zoneFooter = GetClockFooter(city, zone, cityTime);
        if (!city.ShowExchangeDetails)
            return zoneFooter;

        ExchangeTradingCalendar? calendar = calendarSet.TryGetByCityKey(city.Key);
        if (calendar is null)
            return zoneFooter;

        ExchangeCalendarStatus status = _exchangeMarketCalendarService.ResolveStatus(calendar, referenceUtc);
        string statusText = _exchangeMarketCalendarService.FormatCompactStatus(status);
        if (string.IsNullOrWhiteSpace(zoneFooter))
            return statusText;

        return $"{zoneFooter} | {statusText}";
    }

    private void ApplyExchangeCardMarketStatus(ClockCityViewModel city, QuoteSnapshot? quote, DateTimeOffset referenceUtc)
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
            city.MarketStatusText = FormatExchangeCardStatusText(quote, null);
            city.MarketStatusForeground = ResolveMarketStatusBrush(quote?.MarketSession, null);
            return;
        }

        ExchangeCalendarStatus status = _exchangeMarketCalendarService.ResolveStatus(calendar, referenceUtc);
        city.MarketStatusText = FormatExchangeCardStatusText(quote, status);
        city.MarketStatusForeground = ResolveMarketStatusBrush(quote?.MarketSession, status);
    }

    private void ApplyExchangeCardMarketStatus(
        ClockCityViewModel city,
        QuoteSnapshot? quote,
        DateTimeOffset referenceUtc,
        ExchangeCalendarSet calendarSet)
    {
        if (!city.ShowExchangeDetails)
        {
            city.MarketStatusText = string.Empty;
            city.MarketStatusForeground = Brushes.Gainsboro;
            return;
        }

        ExchangeTradingCalendar? calendar = calendarSet.TryGetByCityKey(city.Key);
        if (calendar is null)
        {
            city.MarketStatusText = FormatExchangeCardStatusText(quote, null);
            city.MarketStatusForeground = ResolveMarketStatusBrush(quote?.MarketSession, null);
            return;
        }

        ExchangeCalendarStatus status = _exchangeMarketCalendarService.ResolveStatus(calendar, referenceUtc);
        city.MarketStatusText = FormatExchangeCardStatusText(quote, status);
        city.MarketStatusForeground = ResolveMarketStatusBrush(quote?.MarketSession, status);
    }

    private string BuildPinnedNewYorkStatusBandText(DateTimeOffset referenceUtc)
    {
        if (_clockViewModel is null)
            return "Market (New York): --";

        ClockCityViewModel? city = _clockViewModel.Cities.FirstOrDefault(candidate =>
            candidate.ShowExchangeDetails &&
            string.Equals(candidate.Key, PinnedNycExchangeKey, StringComparison.OrdinalIgnoreCase));
        if (city is null)
            return "Market (New York): --";

        _latestQuotes.TryGetValue(city.ExchangeSymbol, out QuoteSnapshot? quote);
        ExchangeTradingCalendar? calendar = _exchangeCalendars.TryGetByCityKey(city.Key);
        if (quote is null || calendar is null)
            return "Market (New York): --";

        ExchangeCalendarStatus calendarStatus = _exchangeMarketCalendarService.ResolveStatus(calendar, referenceUtc);
        MarketSession effectiveSession = quote.MarketSession == MarketSession.Unknown
            ? calendarStatus.Session
            : quote.MarketSession;
        string sessionText = effectiveSession switch
        {
            MarketSession.Regular => "Regular",
            MarketSession.PreMarket => "Pre-Market",
            MarketSession.AfterHours => "After Hours",
            MarketSession.Closed => "Closed",
            _ => calendarStatus.IsOpen ? "Regular" : "Closed"
        };

        string countdownText = FormatPinnedStatusCountdown(effectiveSession, calendarStatus);

        return FormatStatusBandText($"Market (New York): {sessionText} | {countdownText}");
    }

    private string BuildPinnedNewYorkStatusBandText(
        IReadOnlyList<ClockCityViewModel> cities,
        IReadOnlyDictionary<string, QuoteSnapshot> quotes,
        ExchangeCalendarSet calendarSet,
        DateTimeOffset referenceUtc)
    {
        ClockCityViewModel? city = cities.FirstOrDefault(candidate =>
            candidate.ShowExchangeDetails &&
            string.Equals(candidate.Key, PinnedNycExchangeKey, StringComparison.OrdinalIgnoreCase));
        if (city is null || string.IsNullOrWhiteSpace(city.ExchangeSymbol))
            return "Market (New York): --";

        quotes.TryGetValue(city.ExchangeSymbol, out QuoteSnapshot? quote);
        ExchangeTradingCalendar? calendar = calendarSet.TryGetByCityKey(city.Key);
        if (quote is null || calendar is null)
            return "Market (New York): --";

        ExchangeCalendarStatus calendarStatus = _exchangeMarketCalendarService.ResolveStatus(calendar, referenceUtc);
        MarketSession effectiveSession = quote.MarketSession == MarketSession.Unknown
            ? calendarStatus.Session
            : quote.MarketSession;
        string sessionText = effectiveSession switch
        {
            MarketSession.Regular => "Regular",
            MarketSession.PreMarket => "Pre-Market",
            MarketSession.AfterHours => "After Hours",
            MarketSession.Closed => "Closed",
            _ => calendarStatus.IsOpen ? "Regular" : "Closed"
        };

        string countdownText = FormatPinnedStatusCountdown(effectiveSession, calendarStatus);
        return FormatStatusBandText($"Market (New York): {sessionText} | {countdownText}");
    }

    private static string FormatPinnedStatusCountdown(MarketSession session, ExchangeCalendarStatus status)
    {
        if (!status.HasCountdown)
            return string.Empty;

        return status.CountdownTo switch
        {
            ExchangeCountdownTarget.Close => $"Closing in {FormatHoursAndMinutes(status.Countdown)}",
            ExchangeCountdownTarget.SessionEnd when session == MarketSession.AfterHours => $"After-hours ends in {FormatHoursAndMinutes(status.Countdown)}",
            ExchangeCountdownTarget.SessionEnd => $"Session ends in {FormatHoursAndMinutes(status.Countdown)}",
            ExchangeCountdownTarget.Open => $"Opening in {FormatDaysHoursAndMinutes(status.Countdown)}",
            _ => string.Empty
        };
    }

    private static Brush ResolveMarketStatusBrush(MarketSession? session, ExchangeCalendarStatus? fallbackStatus)
        => (session is null or MarketSession.Unknown ? fallbackStatus?.Session : session) switch
        {
            MarketSession.Regular => Brushes.LimeGreen,
            MarketSession.PreMarket => Brushes.Goldenrod,
            MarketSession.AfterHours => Brushes.SandyBrown,
            MarketSession.Closed => Brushes.OrangeRed,
            _ => fallbackStatus?.IsOpen == true ? Brushes.LimeGreen : fallbackStatus is not null ? Brushes.OrangeRed : Brushes.Gainsboro
        };

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
                ExchangeSymbol = city.ExchangeSymbol,
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

    private async Task LoadBackgroundAsync(string? path, CancellationToken cancellationToken = default)
    {
        _backgroundTransitionCompletionTimer?.Stop();
        _backgroundTransitionCompletionTimer = null;

        if (!IsSupportedBackgroundReference(path))
        {
            _backgroundTransitionInFlight = false;
            _currentBackgroundBitmap = null;
            _committedBackgroundSource = null;
            SetBackgroundZoomRunning(false, "background-cleared");
            if (_activeBackgroundImage is not null)
                _activeBackgroundImage.Source = null;
            if (_inactiveBackgroundImage is not null)
                _inactiveBackgroundImage.Source = null;
            UpdateFooterAttribution(null);
            return;
        }

        string backgroundPath = path!;
        UpdateFooterAttribution(backgroundPath);
        byte[]? preloadedBytes = await PreloadBackgroundBytesAsync(backgroundPath, cancellationToken).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        BitmapImage backgroundBitmap = CreateBackgroundBitmap(backgroundPath, preloadedBytes);
        _currentBackgroundOpacity = GetBackgroundPresentationOpacity(backgroundBitmap);

        if (_activeBackgroundImage is null || _inactiveBackgroundImage is null)
        {
            _currentBackgroundBitmap = backgroundBitmap;
            CanonicalizeBackgroundLayers(backgroundBitmap);
            ResetBackgroundZoomState();
            EnsureBackgroundSlowZoomRunning();
            return;
        }

        if (_activeBackgroundImage.Source is null || string.IsNullOrWhiteSpace(_currentBackgroundPath))
        {
            _backgroundTransitionInFlight = false;
            _currentBackgroundBitmap = backgroundBitmap;
            StopBackgroundAnimations(_activeBackgroundImage);
            StopBackgroundAnimations(_inactiveBackgroundImage);
            ResetBackgroundTransform(_activeBackgroundImage);
            ResetBackgroundTransform(_inactiveBackgroundImage);
            CanonicalizeBackgroundLayers(backgroundBitmap);
            ResetBackgroundZoomState();
            EnsureBackgroundSlowZoomRunning();
            return;
        }

        BeginBackgroundTransition(backgroundPath, backgroundBitmap);
    }


    private void UpdateFooterAttribution(string? backgroundPath)
    {
        if (string.IsNullOrWhiteSpace(backgroundPath) ||
            !_backgroundAttributions.TryGetValue(backgroundPath, out string? attribution) ||
            string.IsNullOrWhiteSpace(attribution))
        {
            if (FooterAttributionWatermark is not null)
                FooterAttributionWatermark.Text = FooterBaseText;
            return;
        }

        if (FooterAttributionWatermark is not null)
            FooterAttributionWatermark.Text = FooterBaseText + " | Image: " + attribution.Trim();
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
            targetGraph.RawLastValue = sourceGraph.RawLastValue;
            targetGraph.QuoteUpdateToken = sourceGraph.QuoteUpdateToken;
            targetGraph.FlashBrush = sourceGraph.FlashBrush;
            targetGraph.IsRefreshTravelFlashActive = sourceGraph.IsRefreshTravelFlashActive;
            targetGraph.RefreshTravelFlashStartedUtc = sourceGraph.RefreshTravelFlashStartedUtc;
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
        graph.RefreshTravelFlashStartedUtc = DateTimeOffset.UtcNow;
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

    private async Task RotateBackgroundAsync(bool forceDifferent = true)
    {
        if (_backgroundTransitionInFlight)
            return;

        if (_backgroundPaths.Count == 0)
        {
            await LoadBackgroundAsync(null).ConfigureAwait(true);
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

        TraceSceneState(
            "BackgroundRotationChosen",
            new KeyValuePair<string, object?>("force_different", forceDifferent),
            new KeyValuePair<string, object?>("shuffle_enabled", _settings.ShuffleBackgrounds),
            new KeyValuePair<string, object?>("candidate_count", candidates.Count),
            new KeyValuePair<string, object?>("chosen_path", Path.GetFileName(nextPath)));
        _currentBackgroundPath = nextPath;
        await LoadBackgroundAsync(nextPath).ConfigureAwait(true);
    }

    private void BeginBackgroundTransition(string path, BitmapImage incomingBitmap)
    {
        if (_activeBackgroundImage is null || _inactiveBackgroundImage is null)
            return;

        _backgroundTransitionInFlight = true;
        _currentBackgroundBitmap = incomingBitmap;
        int transitionGeneration = ++_backgroundTransitionGeneration;
        Image incoming = _inactiveBackgroundImage;
        Image outgoing = _activeBackgroundImage;
        StopBackgroundAnimations(incoming);
        StopBackgroundAnimations(outgoing);
        SetBackgroundZoomRunning(false, "background-transitioning");

        incoming.Source = incomingBitmap;
        incoming.Opacity = 0d;
        ResetBackgroundTransform(incoming);
        ResetBackgroundTransform(outgoing);
        SetBackgroundScale(incoming, _backgroundZoomScale, _backgroundZoomScale);

        IEasingFunction ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        TimeSpan duration = TimeSpan.FromMilliseconds(450);
        AnimateBackgroundProperty(incoming, Image.OpacityProperty, 0d, _currentBackgroundOpacity, duration, ease);

        _backgroundTransitionCompletionTimer?.Stop();
        DispatcherTimer completionTimer = new() { Interval = duration + TimeSpan.FromMilliseconds(100) };
        _backgroundTransitionCompletionTimer = completionTimer;
        completionTimer.Tick += (_, _) =>
        {
            completionTimer.Stop();
            if (!ReferenceEquals(_backgroundTransitionCompletionTimer, completionTimer) ||
                transitionGeneration != _backgroundTransitionGeneration)
            {
                TraceSceneState(
                    "BackgroundTransitionSkipped",
                    new KeyValuePair<string, object?>("path", Path.GetFileName(path)),
                    new KeyValuePair<string, object?>("transition_generation", transitionGeneration),
                    new KeyValuePair<string, object?>("active_generation", _backgroundTransitionGeneration));
                return;
            }

            CanonicalizeBackgroundLayers(incomingBitmap);
            TraceSceneState(
                "BackgroundTransitionComplete",
                new KeyValuePair<string, object?>("path", Path.GetFileName(path)),
                new KeyValuePair<string, object?>("zoom_scale", _backgroundZoomScale));
            EnsureBackgroundSlowZoomRunning();
            _backgroundTransitionCompletionTimer = null;
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
        bool promotedCommittedSource = TryPromoteCommittedBackgroundSource();
        if (!promotedCommittedSource && TryRecoverOrQueueActiveBackgroundSource())
        {
            TraceSceneState(
                "BackgroundSourceRecovered",
                new KeyValuePair<string, object?>("path", Path.GetFileName(_currentBackgroundPath)),
                new KeyValuePair<string, object?>("opacity", _currentBackgroundOpacity));
        }

        if (_activeBackgroundImage?.Source is null)
        {
            SetBackgroundZoomRunning(false, "no-active-background");
            return;
        }

        SetBackgroundZoomRunning(true, "background-active");
    }

    private void StepBackgroundSlowZoom()
    {
        bool promotedCommittedSource = TryPromoteCommittedBackgroundSource();
        if (!promotedCommittedSource && TryRecoverOrQueueActiveBackgroundSource())
        {
            TraceSceneState(
                "BackgroundSourceRecovered",
                new KeyValuePair<string, object?>("path", Path.GetFileName(_currentBackgroundPath)),
                new KeyValuePair<string, object?>("opacity", _currentBackgroundOpacity));
        }

        if (_activeBackgroundImage?.Source is null)
        {
            SetBackgroundZoomRunning(false, "source-missing-during-tick");
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

    private bool TryPromoteCommittedBackgroundSource()
    {
        if (_activeBackgroundImage is null || _committedBackgroundSource is null)
            return false;

        if (_activeBackgroundImage.Source is not null)
            return false;

        CanonicalizeBackgroundLayers(_committedBackgroundSource);
        return true;
    }

    private bool TryRecoverOrQueueActiveBackgroundSource()
    {
        if (_activeBackgroundImage?.Source is not null && _activeBackgroundImage.Opacity > 0.01d)
            return false;

        if (_activeBackgroundImage is null || _inactiveBackgroundImage is null)
            return false;

        ImageSource? recoverySource = _activeBackgroundImage.Source
            ?? _committedBackgroundSource
            ?? _inactiveBackgroundImage.Source
            ?? _currentBackgroundBitmap;
        if (recoverySource is null && IsSupportedBackgroundReference(_currentBackgroundPath))
        {
            QueueBackgroundRecoveryReload(_currentBackgroundPath!);
            return true;
        }

        if (recoverySource is null)
            return false;

        bool sourceWasMissing = _activeBackgroundImage?.Source is null;
        CanonicalizeBackgroundLayers(recoverySource);
        return sourceWasMissing;
    }

    private void QueueBackgroundRecoveryReload(string path)
    {
        if (_backgroundRecoveryReloadInFlight)
            return;

        CancelBackgroundRecoveryReload();
        CancellationTokenSource cancellation = new();
        _backgroundRecoveryReloadCancellation = cancellation;
        _backgroundRecoveryReloadInFlight = true;
        int recoveryGeneration = ++_backgroundRecoveryReloadGeneration;
        _ = ReloadBackgroundForRecoveryAsync(path, cancellation, recoveryGeneration);
    }

    private async Task ReloadBackgroundForRecoveryAsync(string path, CancellationTokenSource cancellation, int recoveryGeneration)
    {
        try
        {
            TraceSceneState(
                "BackgroundRecoveryReloadQueued",
                new KeyValuePair<string, object?>("path", Path.GetFileName(path)));
            await LoadBackgroundAsync(path, cancellation.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            TraceSceneState(
                "BackgroundRecoveryReloadCanceled",
                new KeyValuePair<string, object?>("path", Path.GetFileName(path)));
        }
        catch (Exception ex)
        {
            TraceSceneState(
                "BackgroundRecoveryReloadFailed",
                new KeyValuePair<string, object?>("path", Path.GetFileName(path)),
                new KeyValuePair<string, object?>("error", ex.Message));
        }
        finally
        {
            if (recoveryGeneration == _backgroundRecoveryReloadGeneration)
            {
                _backgroundRecoveryReloadCancellation = null;
                _backgroundRecoveryReloadInFlight = false;
            }
            cancellation.Dispose();
        }
    }

    private void CancelBackgroundRecoveryReload()
    {
        CancellationTokenSource? cancellation = _backgroundRecoveryReloadCancellation;
        if (cancellation is null)
            return;

        _backgroundRecoveryReloadCancellation = null;
        try
        {
            cancellation.Cancel();
        }
        catch (Exception ex)
        {
            TraceSceneState(
                "BackgroundRecoveryReloadCancelFailed",
                new KeyValuePair<string, object?>("error", ex.Message));
        }
    }

    private void FinalizeBackgroundTransition(Image activeImage, Image standbyImage, ImageSource source)
    {
        _backgroundTransitionCompletionTimer?.Stop();
        _backgroundTransitionCompletionTimer = null;
        _backgroundTransitionInFlight = false;
        StopBackgroundAnimations(activeImage);
        StopBackgroundAnimations(standbyImage);
        ResetBackgroundTransform(activeImage);
        ResetBackgroundTransform(standbyImage);
        ImageSource committedSource = CreateStandbyBackgroundSource(source);
        ImageSource standbySource = CreateStandbyBackgroundSource(source);
        _committedBackgroundSource = committedSource;
        activeImage.Source = committedSource;
        activeImage.Opacity = _currentBackgroundOpacity;
        standbyImage.Source = standbySource;
        standbyImage.Opacity = 0d;
        SetBackgroundScale(activeImage, _backgroundZoomScale, _backgroundZoomScale);
        _activeBackgroundImage = activeImage;
        _inactiveBackgroundImage = standbyImage;
    }

    private void CanonicalizeBackgroundLayers(ImageSource source)
    {
        FinalizeBackgroundTransition(BackgroundImageA, BackgroundImageB, source);
    }

    private void SetBackgroundZoomRunning(bool enabled, string reason)
    {
        bool isEnabled = _backgroundZoomTimer.IsEnabled;
        if (enabled)
        {
            if (!isEnabled)
            {
                _backgroundZoomTimer.Start();
                TraceSceneState(
                    "BackgroundZoomStarted",
                    new KeyValuePair<string, object?>("reason", reason),
                    new KeyValuePair<string, object?>("scale", _backgroundZoomScale),
                    new KeyValuePair<string, object?>("path", Path.GetFileName(_currentBackgroundPath)));
            }

            return;
        }

        if (!isEnabled)
            return;

        _backgroundZoomTimer.Stop();
        TraceSceneState(
            "BackgroundZoomStopped",
            new KeyValuePair<string, object?>("reason", reason),
            new KeyValuePair<string, object?>("scale", _backgroundZoomScale),
            new KeyValuePair<string, object?>("path", Path.GetFileName(_currentBackgroundPath)));
    }

    private static BitmapImage CreateBackgroundBitmap(string path, byte[]? preloadedBytes = null)
    {
        if (preloadedBytes is not null || File.Exists(path))
        {
            byte[] bytes = preloadedBytes ?? File.ReadAllBytes(path);
            using MemoryStream memoryStream = new(bytes, writable: false);
            BitmapImage fileBitmap = new();
            fileBitmap.BeginInit();
            fileBitmap.CacheOption = BitmapCacheOption.OnLoad;
            fileBitmap.StreamSource = memoryStream;
            fileBitmap.EndInit();
            if (fileBitmap.CanFreeze)
                fileBitmap.Freeze();
            return fileBitmap;
        }

        BitmapImage bitmap = new();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.EndInit();
        if (bitmap.CanFreeze)
            bitmap.Freeze();
        return bitmap;
    }

    private static ImageSource CreateStandbyBackgroundSource(ImageSource source)
    {
        if (source is BitmapSource bitmapSource)
        {
            BitmapSource clone = bitmapSource.CloneCurrentValue();
            if (clone.CanFreeze)
                clone.Freeze();
            return clone;
        }

        return source;
    }

    private static double GetBackgroundPresentationOpacity(BitmapSource bitmap)
    {
        try
        {
            BitmapSource sampleSource = bitmap;
            if (bitmap.PixelWidth > 48 || bitmap.PixelHeight > 48)
            {
                double scaleX = 48d / Math.Max(1d, bitmap.PixelWidth);
                double scaleY = 48d / Math.Max(1d, bitmap.PixelHeight);
                sampleSource = new TransformedBitmap(bitmap, new ScaleTransform(scaleX, scaleY));
            }

            if (sampleSource.Format != PixelFormats.Bgra32)
                sampleSource = new FormatConvertedBitmap(sampleSource, PixelFormats.Bgra32, null, 0);

            int stride = sampleSource.PixelWidth * 4;
            byte[] pixels = new byte[stride * sampleSource.PixelHeight];
            sampleSource.CopyPixels(pixels, stride, 0);

            double luminanceTotal = 0d;
            int pixelCount = 0;
            for (int index = 0; index < pixels.Length; index += 4)
            {
                byte blue = pixels[index];
                byte green = pixels[index + 1];
                byte red = pixels[index + 2];
                byte alpha = pixels[index + 3];
                if (alpha == 0)
                    continue;

                double luminance = ((0.2126d * red) + (0.7152d * green) + (0.0722d * blue)) / 255d;
                luminanceTotal += luminance;
                pixelCount++;
            }

            if (pixelCount == 0)
                return 0.45d;

            double averageLuminance = luminanceTotal / pixelCount;
            return averageLuminance switch
            {
                < 0.10d => 0.78d,
                < 0.16d => 0.68d,
                < 0.24d => 0.58d,
                _ => 0.45d
            };
        }
        catch
        {
            return 0.45d;
        }
    }

    private static async Task<byte[]?> PreloadBackgroundBytesAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
            return null;

        return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
    }

    private static bool IsSupportedBackgroundReference(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        return File.Exists(path);
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

        if (graph.IsRefreshTravelFlashActive &&
            graph.RefreshTravelFlashStartedUtc > DateTimeOffset.MinValue &&
            DateTimeOffset.UtcNow - graph.RefreshTravelFlashStartedUtc >= GraphRefreshTravelFlashMaximumDuration)
        {
            graph.IsRefreshTravelFlashActive = false;
            graph.RefreshTravelTargetY = null;
            graph.VelocityX = graph.NominalVelocityX == 0d ? graph.VelocityX : graph.NominalVelocityX;
            graph.VelocityY = graph.NominalVelocityY == 0d ? graph.VelocityY : graph.NominalVelocityY;
            return;
        }

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
        _lastFullTapeSyncUtc = DateTimeOffset.UtcNow;
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

    }

    private bool NewsChanged(NewsFlasherViewModel source)
    {
        if (!string.Equals(_newsViewModel.Title, source.Title, StringComparison.Ordinal) ||
            Math.Abs(_newsViewModel.Speed - source.Speed) > 0.001d ||
            _newsViewModel.Headlines.Count != source.Headlines.Count)
        {
            return true;
        }

        for (int index = 0; index < source.Headlines.Count; index++)
        {
            NewsHeadlineViewModel current = _newsViewModel.Headlines[index];
            NewsHeadlineViewModel next = source.Headlines[index];
            if (!string.Equals(current.Text, next.Text, StringComparison.Ordinal) ||
                !Equals(current.Foreground, next.Foreground) ||
                current.IsSupplemental != next.IsSupplemental)
            {
                return true;
            }
        }

        return false;
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
        // Flash only on raw displayed price changes. Percent-only churn is too noisy
        // for the one-symbol runtime cadence and was a visible source of false motion.
        bool valueChanged = !string.Equals(target.LastText, source.LastText, StringComparison.Ordinal);

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

        if (hadPriorSymbol && valueChanged && !string.IsNullOrWhiteSpace(source.LastText))
            target.TriggerValueFlash(source.ChangeForeground);
    }

    private bool ApplyQuotesToDisplayedTapeItems(IEnumerable<QuoteSnapshot> quotes)
    {
        bool allConfiguredTapeSymbolsMatched = true;
        foreach (QuoteSnapshot quote in quotes)
        {
            if (!ApplyQuoteToDisplayedTapeItems(quote))
                allConfiguredTapeSymbolsMatched = false;
        }

        return allConfiguredTapeSymbolsMatched;
    }

    private bool ApplyQuoteToDisplayedTapeItems(QuoteSnapshot quote)
    {
        Debug.Assert(Dispatcher.CheckAccess(), "Displayed tape items must be mutated on the WPF dispatcher thread.");
        if (string.IsNullOrWhiteSpace(quote.Symbol))
            return true;

        decimal? last = quote.Last ?? quote.PreviousClose;
        decimal? percent = quote.ChangePercent;
        bool hasUsableValue = last is not null;
        if (!hasUsableValue)
            return !IsConfiguredTapeSymbol(quote.Symbol);

        string lastText = last is decimal lastValue
            ? lastValue.ToString("0.00", CultureInfo.InvariantCulture)
            : string.Empty;
        string percentText = percent is decimal percentValue
            ? $"{(percentValue >= 0 ? "+" : string.Empty)}{percentValue:0.00}%"
            : string.Empty;
        Brush changeBrush = percent switch
        {
            > 0 => Brushes.LimeGreen,
            < 0 => Brushes.OrangeRed,
            _ => Brushes.Gainsboro
        };

        bool matchedDisplayedItem = false;
        foreach (TapeItemViewModel item in _tapes.SelectMany(tape => tape.Items))
        {
            if (!string.Equals(item.SymbolText, quote.Symbol, StringComparison.OrdinalIgnoreCase))
                continue;

            matchedDisplayedItem = true;
            bool valueChanged = !string.Equals(item.LastText, lastText, StringComparison.Ordinal);
            item.LastText = lastText;
            item.ChangeText = percentText;
            item.IsWaitingOnData = false;
            item.HasMissingData = false;
            item.WaitingGlyphText = string.Empty;
            item.LastForeground = Brushes.WhiteSmoke;
            item.ChangeForeground = changeBrush;
            item.QuoteUpdateToken = quote.FetchTimestampUtc != default ? quote.FetchTimestampUtc.UtcTicks : 0;

            if (valueChanged && hasUsableValue)
                item.TriggerValueFlash(changeBrush);
        }

        return matchedDisplayedItem || !IsConfiguredTapeSymbol(quote.Symbol);
    }

    private bool IsConfiguredTapeSymbol(string symbol)
    {
        return _settings.Groups
            .Where(group => group.Enabled)
            .SelectMany(group => group.Tickers)
            .Any(ticker => ticker.Enabled && string.Equals(ticker.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
    }

    private void TraceDisplayedTapeSampleIfDue()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (now - _lastDisplayedTapeSampleTraceUtc < TimeSpan.FromSeconds(15))
            return;

        _lastDisplayedTapeSampleTraceUtc = now;
        TraceDisplayedTapeSample();
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
                        ? "loading"
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

        List<string> laneSnapshots = _tapes
            .Select((tape, index) =>
            {
                List<string> entries = tape.Items
                    .Where(item => !string.IsNullOrWhiteSpace(item.SymbolText))
                    .Take(8)
                    .Select(item =>
                    {
                        string state = item.HasMissingData
                            ? "missing"
                            : item.IsWaitingOnData
                                ? "loading"
                                : "live";
                        return $"{item.SymbolText}:{NormalizeTapeSnapshotValue(item.LastText)}:{state}";
                    })
                    .ToList();

                return $"lane{index + 1}={tape.Title}[{string.Join(", ", entries)}]";
            })
            .ToList();

        TraceSceneState(
            "DisplayedTapeLanes",
            new KeyValuePair<string, object?>("lane_count", laneSnapshots.Count),
            new KeyValuePair<string, object?>("lanes", laneSnapshots));
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

    private static AppSettings CloneNewsSettings(AppSettings source) => new()
    {
        NewsScrollerMode = source.NewsScrollerMode,
        DeepSeekWritingStyle = source.DeepSeekWritingStyle,
        NewsFeedUrl = source.NewsFeedUrl,
        NewsRefreshMinutes = source.NewsRefreshMinutes,
        DeepSeekApiKey = source.DeepSeekApiKey,
        DeepSeekEndpointUrl = source.DeepSeekEndpointUrl,
        DeepSeekModelId = source.DeepSeekModelId,
        HttpTimeoutSeconds = source.HttpTimeoutSeconds
    };

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

        decimal? last = quote.Last ?? quote.PreviousClose;
        decimal? percent = quote.ChangePercent;
        string lastText = last is decimal lastValue ? lastValue.ToString("0.00") : string.Empty;
        string changeText = percent is decimal percentValue
            ? $"{(percentValue >= 0 ? "+" : string.Empty)}{percentValue:0.00}%"
            : string.Empty;

        if (quote.IsStale)
        {
            graph.IsVisible = !string.IsNullOrWhiteSpace(lastText) ||
                              graph.Points.Count > 1 ||
                              graph.GreenSegments.Count > 0 ||
                              graph.RedSegments.Count > 0;
            if (!string.IsNullOrWhiteSpace(lastText))
                graph.LastText = lastText;
            if (!string.IsNullOrWhiteSpace(changeText))
                graph.ChangeText = changeText;
            graph.ChangeForeground = Brushes.Goldenrod;
            graph.LatestSegmentBrush = Brushes.Goldenrod;
            return;
        }

        graph.IsVisible = true;
        Brush changeBrush = percent switch
        {
            > 0m => Brushes.LimeGreen,
            < 0m => Brushes.OrangeRed,
            _ => Brushes.Gainsboro
        };

        bool hadPriorSymbol = !string.IsNullOrWhiteSpace(graph.Symbol);
        bool rawPriceChanged = graph.RawLastValue != last;
        long quoteUpdateToken = quote.FetchTimestampUtc.UtcTicks;

        graph.LastText = lastText;
        graph.ChangeText = changeText;
        graph.ChangeForeground = changeBrush;
        graph.LatestSegmentBrush = changeBrush;
        graph.QuoteUpdateToken = quoteUpdateToken;
        graph.RawLastValue = last;

        if (hadPriorSymbol && rawPriceChanged && !string.IsNullOrWhiteSpace(lastText))
        {
            if (graph.IsRefreshTravelFlashActive)
            {
                TraceScene($"GraphCardFlashSkipped symbol={graph.Symbol} raw_last={(last?.ToString("0.####") ?? "--")} percent={(percent?.ToString("0.####") ?? "--")} reason=refresh-travel-active");
                return;
            }

            ApplyRefreshMotionCue(graph, percent);
            graph.TriggerCardFlash(changeBrush);
            TraceScene($"GraphCardFlash symbol={graph.Symbol} raw_last={(last?.ToString("0.####") ?? "--")} percent={(percent?.ToString("0.####") ?? "--")} reason=raw-price-change");
        }
    }

    private void UpdateStatusFreshnessText()
    {
        if (_statusViewModel is null)
            return;

        if (StartupCoordinator.TryGetLatestUpdatedSymbol(_latestQuotes, out string latestUpdatedSymbol, out DateTimeOffset latestUpdatedFetchUtc))
        {
            _statusViewModel.UpdatedPrefixText = "Last Updated:";
            decimal? changePercent = _latestQuotes.TryGetValue(latestUpdatedSymbol, out QuoteSnapshot? updatedQuote)
                ? updatedQuote.ChangePercent
                : null;
            _statusViewModel.UpdatedTickerFieldText = StartupCoordinator.FormatUpdatedTickerField(latestUpdatedSymbol, changePercent, latestUpdatedFetchUtc);
            _statusViewModel.UpdatedTickerFieldForeground = StartupCoordinator.ResolveUpdatedTickerFieldBrush(changePercent);
            UpdateDataFreshnessStatus();
            return;
        }

        _statusViewModel.UpdatedPrefixText = "Last Updated:";
        _statusViewModel.UpdatedTickerFieldText = StartupCoordinator.FormatUpdatedTickerField(null, null, DateTimeOffset.MinValue);
        _statusViewModel.UpdatedTickerFieldForeground = Brushes.Gainsboro;
        UpdateDataFreshnessStatus();
    }

    private void UpdateDataFreshnessStatus()
    {
        if (_statusViewModel is null)
            return;

        bool networkAvailable = StartupCoordinator.ResolveEffectiveDataFreshnessNetworkState(
            _networkAvailabilityService.IsNetworkAvailable(),
            ReadRuntimeQuoteFailureStreak(),
            RuntimeQuoteOfflineFailureThreshold);
        _statusViewModel.DataFreshnessText = StartupCoordinator.ResolveDataFreshnessText(networkAvailable, _latestQuotes);
        _statusViewModel.DataFreshnessForeground = StartupCoordinator.ResolveDataFreshnessBrush(networkAvailable, _latestQuotes);
    }

    private void ApplyCompletedRuntimeQuote(string symbol, Task<IReadOnlyList<QuoteSnapshot>> task)
    {
        if (!_inFlightQuoteRequests.TryComplete(symbol, task, out _))
        {
            TraceSceneState(
                "RuntimeQuoteRequestCompletionIgnored",
                new KeyValuePair<string, object?>("symbol", symbol),
                new KeyValuePair<string, object?>("reason", "stale_or_pruned"),
                new KeyValuePair<string, object?>("in_flight_count", _inFlightQuoteRequests.Count));
            return;
        }

        if (_isValidationPaused)
            return;

        IReadOnlyList<QuoteSnapshot> quotes;
        try
        {
            quotes = task.GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            bool failureCounted = !_isValidationPaused && ex is not OperationCanceledException;
            if (failureCounted)
            {
                int failureStreak = IncrementRuntimeQuoteFailureStreak();
                UpdateStatusFreshnessText();
                ResetRuntimeQuoteTransportIfNeeded("request-failure");
                TraceRuntimeQuoteRequestFailed(symbol, ex, failureCounted, failureStreak);
                return;
            }
            TraceRuntimeQuoteRequestFailed(symbol, ex, failureCounted, ReadRuntimeQuoteFailureStreak());
            return;
        }

        if (quotes.Count == 0)
        {
            ResetRuntimeQuoteFailureStreak();
            UpdateStatusFreshnessText();
            return;
        }

        Dictionary<string, QuoteSnapshot> deltaQuotes = new(StringComparer.OrdinalIgnoreCase);
        foreach (QuoteSnapshot quote in quotes)
            deltaQuotes[quote.Symbol] = quote;

        IReadOnlyDictionary<string, QuoteSnapshot> previousQuotes = _latestQuotes;
        _latestQuotes = MergeQuotes(_latestQuotes, deltaQuotes);
        _startupCoordinator.PrimeRuntimeQuotes(deltaQuotes);
        ResetRuntimeQuoteFailureStreak();

        bool tapeStructureStillMatched = ApplyQuotesToDisplayedTapeItems(deltaQuotes.Values);
        // Config apply performs a full scene/tape sync immediately. During ordinary
        // quote flow, keep the hot path surgical and run only a short structural
        // hygiene sync to catch unexpected drift without rebuilding every tick.
        if (!tapeStructureStillMatched || DateTimeOffset.UtcNow - _lastFullTapeSyncUtc > RuntimeTapeStructuralSyncInterval)
            SyncTapes(_startupCoordinator.BuildTapesForQuotes(_settings, _latestQuotes));
        UpdateStatusFreshnessText();
        if (HasMeaningfulMacroDelta(previousQuotes, deltaQuotes))
            QueueMacroRefresh("quote-delta");

        foreach (FloatingGraphViewModel graph in _graphs.Where(graph => deltaQuotes.ContainsKey(graph.Symbol)))
            ApplyQuoteToGraph(graph);

        if (HasMeaningfulWorldMarketDelta(previousQuotes, deltaQuotes))
            QueueWorldMarketsRefresh(refreshAncillary: false, reason: "quote-delta");

        TraceDisplayedTapeSampleIfDue();
        DateTimeOffset latestFetchUtc = deltaQuotes.Values
            .Where(quote => quote.FetchTimestampUtc > DateTimeOffset.MinValue)
            .Select(quote => quote.FetchTimestampUtc)
            .DefaultIfEmpty(DateTimeOffset.MinValue)
            .Max();
        TraceSceneState(
            "RuntimeQuoteApplied",
            new KeyValuePair<string, object?>("requested_symbol", symbol),
            new KeyValuePair<string, object?>("data_freshness_text", _statusViewModel?.DataFreshnessText),
            new KeyValuePair<string, object?>("latest_fetch_timestamp_utc", latestFetchUtc == DateTimeOffset.MinValue ? null : latestFetchUtc),
            new KeyValuePair<string, object?>("latest_fetch_age_seconds", latestFetchUtc == DateTimeOffset.MinValue ? null : Math.Round(Math.Max(0d, (DateTimeOffset.UtcNow - latestFetchUtc).TotalSeconds), 1)),
            new KeyValuePair<string, object?>("resolved_symbols", deltaQuotes.Keys.ToList()),
            new KeyValuePair<string, object?>("in_flight_count", _inFlightQuoteRequests.Count));
    }

    private void TraceRuntimeQuoteRequestFailed(string symbol, Exception ex, bool failureCounted, int failureStreak)
    {
        TraceSceneState(
            "RuntimeQuoteRequestFailed",
            new KeyValuePair<string, object?>("symbol", symbol),
            new KeyValuePair<string, object?>("message", ex.Message),
            new KeyValuePair<string, object?>("failure_counted", failureCounted),
            new KeyValuePair<string, object?>("failure_streak", failureStreak),
            new KeyValuePair<string, object?>("data_freshness_text", _statusViewModel?.DataFreshnessText),
            new KeyValuePair<string, object?>("in_flight_count", _inFlightQuoteRequests.Count));
    }

    private void SyncStatusViewModel(StatusBarViewModel source, bool forceMacroRefresh)
    {
        if (_statusViewModel is null)
        {
            _statusViewModel = source;
            StatusBarHost.DataContext = _statusViewModel;
        }
        else
        {
            _statusViewModel.MarketStatusText = source.MarketStatusText;
            _statusViewModel.UpdatedPrefixText = source.UpdatedPrefixText;
            _statusViewModel.UpdatedTickerFieldText = source.UpdatedTickerFieldText;
            _statusViewModel.UpdatedTickerFieldForeground = source.UpdatedTickerFieldForeground;
            _statusViewModel.ClockDateText = source.ClockDateText;
            _statusViewModel.ClockText = source.ClockText;
            UpdateDataFreshnessStatus();
        }
        EnsureMacroMetersInitialized();
        if (forceMacroRefresh)
            QueueMacroRefresh("scene-sync");
    }

    private static bool IsMacroSymbol(string symbol)
        => symbol is "^VIX" or "^IXIC" or "^TNX" or "^IRX" or "GC=F" or "BZ=F" or "DX-Y.NYB" or "BTC-USD";

    private bool IsClockMarketSymbol(string symbol)
        => _clockViewModel?.Cities.Any(city => string.Equals(city.ExchangeSymbol, symbol, StringComparison.OrdinalIgnoreCase)) ?? false;

    private bool HasMeaningfulMacroDelta(
        IReadOnlyDictionary<string, QuoteSnapshot> existingQuotes,
        IReadOnlyDictionary<string, QuoteSnapshot> deltaQuotes)
        => deltaQuotes.Any(pair => IsMacroSymbol(pair.Key) && HasMeaningfulQuoteDelta(existingQuotes, pair.Key, pair.Value));

    private bool HasMeaningfulWorldMarketDelta(
        IReadOnlyDictionary<string, QuoteSnapshot> existingQuotes,
        IReadOnlyDictionary<string, QuoteSnapshot> deltaQuotes)
        => deltaQuotes.Any(pair => IsClockMarketSymbol(pair.Key) && HasMeaningfulQuoteDelta(existingQuotes, pair.Key, pair.Value));

    private static bool HasMeaningfulQuoteDelta(
        IReadOnlyDictionary<string, QuoteSnapshot> existingQuotes,
        string symbol,
        QuoteSnapshot incoming)
    {
        if (!existingQuotes.TryGetValue(symbol, out QuoteSnapshot? existing))
            return true;

        return existing.Last != incoming.Last ||
               existing.PreviousClose != incoming.PreviousClose ||
               existing.Change != incoming.Change ||
               existing.ChangePercent != incoming.ChangePercent ||
               existing.IsStale != incoming.IsStale ||
               existing.MarketSession != incoming.MarketSession;
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
        List<FloatingGraphViewModel> visibleGraphs = EnumerateVisibleGraphCards().ToList();
        HashSet<string> visibleKeys = visibleGraphs
            .Select(GetGraphKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (string staleKey in _graphControlsByKey.Keys.Where(key => !visibleKeys.Contains(key)).ToList())
        {
            FloatingGraphControl staleControl = _graphControlsByKey[staleKey];
            staleControl.DataContext = null;
            FloatingGraphCanvas.Children.Remove(staleControl);
            _graphControlsByKey.Remove(staleKey);
        }

        for (int index = 0; index < visibleGraphs.Count; index++)
        {
            FloatingGraphViewModel graph = visibleGraphs[index];
            string graphKey = GetGraphKey(graph);
            if (_graphControlsByKey.TryGetValue(graphKey, out FloatingGraphControl? control))
            {
                if (!ReferenceEquals(control.DataContext, graph))
                    control.DataContext = graph;
            }
            else
            {
                control = new FloatingGraphControl
                {
                    DataContext = graph
                };
                control.SetBinding(Canvas.LeftProperty, new Binding(nameof(FloatingGraphViewModel.X)));
                control.SetBinding(Canvas.TopProperty, new Binding(nameof(FloatingGraphViewModel.Y)));
                Panel.SetZIndex(control, 12);
                _graphControlsByKey[graphKey] = control;
                FloatingGraphCanvas.Children.Insert(Math.Min(index, FloatingGraphCanvas.Children.Count), control);
                continue;
            }

            int currentIndex = FloatingGraphCanvas.Children.IndexOf(control);
            if (currentIndex >= 0 && currentIndex != index)
            {
                FloatingGraphCanvas.Children.Remove(control);
                FloatingGraphCanvas.Children.Insert(Math.Min(index, FloatingGraphCanvas.Children.Count), control);
            }
        }
    }

}






