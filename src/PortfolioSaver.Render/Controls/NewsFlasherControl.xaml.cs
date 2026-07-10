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
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PortfolioSaver.Render.ViewModels;
using PortfolioSaver.Shared.Diagnostics;

namespace PortfolioSaver.Render.Controls;

public partial class NewsFlasherControl : UserControl
{
    private const double MaxVisibleHeadlineHeight = 38d;
    private const double VisibleLineHeight = 19d;
    private const double DefaultRevealPauseSeconds = 0.35d;
    private const double DefaultPostScrollPauseSeconds = 0.25d;
    private const double DefaultBetweenHeadlinePauseSeconds = 1.6d;
    private const double TelegraphVerticalScrollPixelsPerSecond = 42d;
    private static readonly TimeSpan RefreshDebounceInterval = TimeSpan.FromMilliseconds(500);
    private const int TypewriterCharactersPerTick = 2;
    private const int MaxWidthMeasurementCacheEntries = 256;
    private const string TeleprinterCursor = " █";
    private readonly DispatcherTimer _playbackTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly DispatcherTimer _refreshDebounceTimer = new() { Interval = RefreshDebounceInterval };
    private NewsFlasherViewModel? _flasher;
    private int _headlineIndex;
    private int _visibleCharacterCount;
    private int _pauseTicksRemaining;
    private int _segmentIndex;
    private double _currentVerticalOffset;
    private double _activeHeadlineHeight;
    private PlaybackPhase _phase = PlaybackPhase.Idle;
    private PlaybackPhase _lastTracedPhase = PlaybackPhase.Idle;
    private bool _pendingRefresh;
    private bool _resetToFirstHeadlineOnRefresh;
    private bool _isUnloaded;
    private IReadOnlyList<string> _wrappedLines = [];
    private readonly Dictionary<MeasurementCacheKey, double> _headlineWidthCache = [];
    private readonly object _headlineWidthCacheGate = new();
    private string _displayTopLine = string.Empty;
    private string _displayBottomLine = string.Empty;
    private string _activeText = string.Empty;
    private Brush _activeForeground = Brushes.WhiteSmoke;
    private bool _awaitingViewport;
    private bool _layoutUpdatedSubscribed;
    private int _headlinePreparationGeneration;
    private Task? _headlinePreparationTask;
    private CancellationTokenSource? _headlinePreparationCancellation;
    private string? _headlinePreparationText;

    public NewsFlasherControl()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += (_, _) =>
        {
            CancelHeadlinePreparation();
            ClearMeasurementCache();
            RefreshLayoutForCurrentState();
            RecoverPlaybackWhenViewportReady();
        };
        SubscribeToLayoutUpdated();
        DataContextChanged += OnDataContextChanged;
        _playbackTimer.Tick += OnPlaybackTick;
        _refreshDebounceTimer.Tick += OnRefreshDebounceElapsed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isUnloaded = false;
        SubscribeToLayoutUpdated();
        _awaitingViewport = false;
        if (_flasher is null)
            SubscribeToFlasher(DataContext as NewsFlasherViewModel);

        ResetPlayback();
        _playbackTimer.Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isUnloaded = true;
        _playbackTimer.Stop();
        _refreshDebounceTimer.Stop();
        _awaitingViewport = false;
        CancelHeadlinePreparation();
        UnsubscribeFromLayoutUpdated();
        UnsubscribeFromFlasher(_flasher);
    }

    private void SubscribeToLayoutUpdated()
    {
        if (_layoutUpdatedSubscribed)
            return;

        LayoutUpdated += OnLayoutUpdated;
        _layoutUpdatedSubscribed = true;
    }

    private void UnsubscribeFromLayoutUpdated()
    {
        if (!_layoutUpdatedSubscribed)
            return;

        LayoutUpdated -= OnLayoutUpdated;
        _layoutUpdatedSubscribed = false;
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        RecoverPlaybackWhenViewportReady();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OldValue, e.NewValue))
        {
            UnsubscribeFromFlasher(e.OldValue as NewsFlasherViewModel);
            SubscribeToFlasher(e.NewValue as NewsFlasherViewModel);
        }

        ResetPlayback();
    }

    private void SubscribeToFlasher(NewsFlasherViewModel? flasher)
    {
        _flasher = flasher;
        if (_flasher is null)
            return;

        _flasher.PropertyChanged += OnFlasherPropertyChanged;
        _flasher.Headlines.CollectionChanged += OnHeadlinesCollectionChanged;
        foreach (NewsHeadlineViewModel headline in _flasher.Headlines)
            headline.PropertyChanged += OnHeadlinePropertyChanged;
    }

    private void UnsubscribeFromFlasher(NewsFlasherViewModel? flasher)
    {
        if (flasher is null)
            return;

        flasher.PropertyChanged -= OnFlasherPropertyChanged;
        flasher.Headlines.CollectionChanged -= OnHeadlinesCollectionChanged;
        foreach (NewsHeadlineViewModel headline in flasher.Headlines)
            headline.PropertyChanged -= OnHeadlinePropertyChanged;

        if (ReferenceEquals(_flasher, flasher))
            _flasher = null;
    }

    private void OnFlasherPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NewsFlasherViewModel.Speed))
            RequestRefresh(resetToFirstHeadline: false);
    }

    private void OnHeadlinesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (NewsHeadlineViewModel headline in e.OldItems)
                headline.PropertyChanged -= OnHeadlinePropertyChanged;
        }

        if (e.NewItems is not null)
        {
            foreach (NewsHeadlineViewModel headline in e.NewItems)
                headline.PropertyChanged += OnHeadlinePropertyChanged;
        }

        RequestRefresh(resetToFirstHeadline: true);
    }

    private void OnHeadlinePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NewsHeadlineViewModel.Text) or nameof(NewsHeadlineViewModel.Foreground))
            RequestRefresh(resetToFirstHeadline: true);
    }

    private void OnPlaybackTick(object? sender, EventArgs e)
    {
        if (!IsViewportReady())
        {
            PausePlaybackUntilViewportReady();
            return;
        }

        _awaitingViewport = false;
        IReadOnlyList<NewsHeadlineViewModel> headlines = GetPlaybackHeadlines();
        if (headlines.Count == 0)
        {
            ClearDisplay();
            return;
        }

        if (_phase == PlaybackPhase.Idle)
        {
            StartHeadlinePreparation(headlines[_headlineIndex % headlines.Count]);
            return;
        }

        switch (_phase)
        {
            case PlaybackPhase.Typing:
                StepTyping();
                break;
            case PlaybackPhase.PauseBeforeScroll:
                StepPause(PlaybackPhase.Scrolling, includeCursor: true);
                break;
            case PlaybackPhase.Scrolling:
                StepScrolling();
                break;
            case PlaybackPhase.PauseAfterScroll:
                StepPause(GetPostScrollNextPhase(), includeCursor: false);
                break;
            case PlaybackPhase.PauseBetweenHeadlines:
                StepPause(PlaybackPhase.AdvanceHeadline, includeCursor: false);
                break;
            case PlaybackPhase.AdvanceHeadline:
                StepAdvanceHeadline(headlines.Count);
                break;
        }
    }

    private IReadOnlyList<NewsHeadlineViewModel> GetPlaybackHeadlines()
        => _flasher?.Headlines
            .Where(item => !string.IsNullOrWhiteSpace(item.Text))
            .ToList() ?? [];

    private void PrepareHeadline(NewsHeadlineViewModel headline)
    {
        if (!IsViewportReady())
        {
            return;
        }

        CancelHeadlinePreparation();
        PreparedHeadline prepared = BuildPreparedHeadline(
            FormatHeadline(headline.Text),
            CreatePreparationContext(),
            _headlineWidthCache,
            _headlineWidthCacheGate,
            CancellationToken.None);
        ApplyPreparedHeadline(prepared, headline.Foreground);
    }

    private void StartHeadlinePreparation(NewsHeadlineViewModel headline)
    {
        if (!IsViewportReady())
            return;

        string activeText = FormatHeadline(headline.Text);
        if (_headlinePreparationTask is { IsCompleted: false } &&
            string.Equals(_headlinePreparationText, activeText, StringComparison.Ordinal))
            return;

        CancelHeadlinePreparation();
        int generation = ++_headlinePreparationGeneration;
        Brush foreground = headline.Foreground;
        HeadlinePreparationContext context = CreatePreparationContext();
        CancellationTokenSource cancellationSource = new();
        _headlinePreparationCancellation = cancellationSource;
        _headlinePreparationText = activeText;
        _headlinePreparationTask = PrepareHeadlineAsync(activeText, foreground, context, generation, cancellationSource);
    }

    private async Task PrepareHeadlineAsync(
        string activeText,
        Brush foreground,
        HeadlinePreparationContext context,
        int generation,
        CancellationTokenSource cancellationSource)
    {
        CancellationToken cancellation = cancellationSource.Token;
        try
        {
            PreparedHeadline prepared = await Task.Run(
                () => BuildPreparedHeadline(activeText, context, _headlineWidthCache, _headlineWidthCacheGate, cancellation),
                cancellation).ConfigureAwait(false);
            await Dispatcher.InvokeAsync(() =>
            {
                if (generation != _headlinePreparationGeneration)
                {
                    cancellationSource.Dispose();
                    return;
                }

                _headlinePreparationTask = null;
                if (ReferenceEquals(_headlinePreparationCancellation, cancellationSource))
                    _headlinePreparationCancellation = null;
                if (string.Equals(_headlinePreparationText, prepared.ActiveText, StringComparison.Ordinal))
                    _headlinePreparationText = null;
                cancellationSource.Dispose();
                ApplyPreparedHeadline(prepared, foreground);
            });
        }
        catch (OperationCanceledException)
        {
            try
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    if (generation == _headlinePreparationGeneration)
                        _headlinePreparationTask = null;
                    if (ReferenceEquals(_headlinePreparationCancellation, cancellationSource))
                        _headlinePreparationCancellation = null;
                    if (string.Equals(_headlinePreparationText, activeText, StringComparison.Ordinal))
                        _headlinePreparationText = null;
                    cancellationSource.Dispose();
                });
            }
            catch
            {
                cancellationSource.Dispose();
            }
        }
        catch (Exception ex)
        {
            try
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    if (generation == _headlinePreparationGeneration)
                        _headlinePreparationTask = null;
                    if (ReferenceEquals(_headlinePreparationCancellation, cancellationSource))
                        _headlinePreparationCancellation = null;
                    if (string.Equals(_headlinePreparationText, activeText, StringComparison.Ordinal))
                        _headlinePreparationText = null;
                    cancellationSource.Dispose();
                    TraceLog.InfoState(
                        "NewsFlasher",
                        "PrepareHeadlineAsyncFailed",
                        [new("error", ex.ToString())]);
                });
            }
            catch
            {
                cancellationSource.Dispose();
            }
        }
    }

    private static PreparedHeadline BuildPreparedHeadline(
        string activeText,
        HeadlinePreparationContext context,
        IDictionary<MeasurementCacheKey, double>? measurementCache,
        object? measurementCacheGate,
        CancellationToken cancellationToken)
        => new(activeText, BuildWrappedLines(activeText, context, measurementCache, measurementCacheGate, cancellationToken));

    private void ApplyPreparedHeadline(PreparedHeadline prepared, Brush foreground)
    {
        _activeText = prepared.ActiveText;
        _wrappedLines = prepared.WrappedLines;
        _segmentIndex = 0;
        _activeForeground = foreground;
        TraceLog.InfoState(
            "NewsFlasher",
            "PrepareHeadline",
            [
                new("headline_index", _headlineIndex),
                new("text_length", _activeText.Length),
                new("segment_count", GetSegmentCount()),
                new("viewport_width", Math.Round(ViewportHost.ActualWidth, 1))
            ]);
        PrepareCurrentSegment();
    }

    private void CancelHeadlinePreparation()
    {
        _headlinePreparationCancellation?.Cancel();
        _headlinePreparationCancellation = null;
        _headlinePreparationTask = null;
        _headlinePreparationText = null;
        _headlinePreparationGeneration++;
    }

    private void PrepareCurrentSegment()
    {
        _displayTopLine = _wrappedLines.Count > _segmentIndex
            ? _wrappedLines[_segmentIndex]
            : string.Empty;
        _displayBottomLine = _wrappedLines.Count > _segmentIndex + 1
            ? _wrappedLines[_segmentIndex + 1]
            : string.Empty;
        _visibleCharacterCount = 0;
        _pauseTicksRemaining = 0;
        _currentVerticalOffset = 0d;
        SetPhase(PlaybackPhase.Typing);
        ActiveHeadlineBlock.Foreground = _activeForeground;
        ActiveHeadlineBlock.Width = GetSafeViewportWidth();
        Canvas.SetLeft(ActiveHeadlineBlock, 0d);
        Canvas.SetTop(ActiveHeadlineBlock, 0d);
        SetDisplayedHeadlineText(BuildVisibleText(includeCursor: true));
        _activeHeadlineHeight = MeasureHeadlineHeight(GetFullSegmentText());
        TraceLog.InfoState(
            "NewsFlasher",
            "PrepareSegment",
            [
                new("headline_index", _headlineIndex),
                new("segment_index", _segmentIndex),
                new("segment_length", GetFullSegmentText().Length),
                new("measured_height", _activeHeadlineHeight)
            ]);
    }

    private void StepTyping()
    {
        if (string.IsNullOrWhiteSpace(GetFullSegmentText()))
        {
            SetPhase(PlaybackPhase.AdvanceHeadline);
            return;
        }

        if (_segmentIndex == 0 && string.IsNullOrEmpty(_displayTopLine))
        {
            SetPhase(PlaybackPhase.AdvanceHeadline);
            return;
        }

        _visibleCharacterCount = Math.Min(GetTypingTargetLength(), _visibleCharacterCount + TypewriterCharactersPerTick);

        SetDisplayedHeadlineText(BuildVisibleText(includeCursor: _visibleCharacterCount < GetTypingTargetLength()));
        ActiveHeadlineBlock.Foreground = _activeForeground;
        ActiveHeadlineBlock.Width = GetSafeViewportWidth();
        Canvas.SetLeft(ActiveHeadlineBlock, 0d);
        Canvas.SetTop(ActiveHeadlineBlock, 0d);

        if (_visibleCharacterCount < GetTypingTargetLength())
            return;

        SetPhase(PlaybackPhase.PauseBeforeScroll);
        _pauseTicksRemaining = GetPauseTicks(DefaultRevealPauseSeconds);
    }

    private void StepScrolling()
    {
        double pixelsPerTick = TelegraphVerticalScrollPixelsPerSecond * (_playbackTimer.Interval.TotalSeconds * Math.Max(0.7d, _flasher?.Speed ?? 1d));
        double lineShift = GetFullSegmentText().Contains(Environment.NewLine, StringComparison.Ordinal)
            ? VisibleLineHeight
            : 0d;
        double targetOffset = -lineShift;
        _currentVerticalOffset = Math.Max(targetOffset, _currentVerticalOffset - pixelsPerTick);
        SetDisplayedHeadlineText(GetFullSegmentText());
        Canvas.SetLeft(ActiveHeadlineBlock, 0d);
        Canvas.SetTop(ActiveHeadlineBlock, _currentVerticalOffset);

        if (_currentVerticalOffset <= targetOffset + 0.1d)
        {
            PlaybackPhase nextPhase = GetPostScrollNextPhase();
            SetPhase(nextPhase == PlaybackPhase.PauseBetweenHeadlines
                ? PlaybackPhase.PauseBetweenHeadlines
                : PlaybackPhase.PauseAfterScroll);
            _pauseTicksRemaining = GetPauseTicks(nextPhase == PlaybackPhase.PauseBetweenHeadlines
                ? DefaultBetweenHeadlinePauseSeconds
                : DefaultPostScrollPauseSeconds);
        }
    }

    private void StepPause(PlaybackPhase nextPhase, bool includeCursor)
    {
        if (_pauseTicksRemaining > 0)
        {
            SetDisplayedHeadlineText(includeCursor ? BuildVisibleText(includeCursor: true) : GetFullSegmentText());
            _pauseTicksRemaining--;
            return;
        }

        SetPhase(nextPhase);
    }

    private void StepAdvanceHeadline(int headlineCount)
    {
        if (_pendingRefresh)
        {
            _pendingRefresh = false;
            _headlineIndex = 0;
            SetPhase(PlaybackPhase.Idle);
            return;
        }

        if (_segmentIndex + 1 < GetSegmentCount())
        {
            _segmentIndex++;
            PrepareCurrentSegment();
            return;
        }

        _headlineIndex = (_headlineIndex + 1) % Math.Max(1, headlineCount);
        SetPhase(PlaybackPhase.Idle);
    }

    private PlaybackPhase GetPostScrollNextPhase()
        => _segmentIndex + 1 < GetSegmentCount()
            ? PlaybackPhase.AdvanceHeadline
            : PlaybackPhase.PauseBetweenHeadlines;

    private void ResetPlayback() => ResetPlaybackCore(preserveHeadlineIndex: false);

    private void ResetPlaybackCore(bool preserveHeadlineIndex)
    {
        _refreshDebounceTimer.Stop();
        _pendingRefresh = false;
        _resetToFirstHeadlineOnRefresh = false;
        if (!preserveHeadlineIndex)
            _headlineIndex = 0;
        _phase = PlaybackPhase.Idle;
        _lastTracedPhase = PlaybackPhase.Idle;
        _visibleCharacterCount = 0;
        _pauseTicksRemaining = 0;
        _segmentIndex = 0;
        _currentVerticalOffset = 0d;
        _wrappedLines = [];
        _displayTopLine = string.Empty;
        _displayBottomLine = string.Empty;
        _activeText = string.Empty;
        _activeHeadlineHeight = 0d;
        _awaitingViewport = false;
        CancelHeadlinePreparation();
        ClearMeasurementCache();
        ClearDisplay();
    }

    private void RequestRefresh(bool resetToFirstHeadline)
    {
        CancelHeadlinePreparation();
        _pendingRefresh = true;
        _resetToFirstHeadlineOnRefresh |= resetToFirstHeadline;
        if (_phase == PlaybackPhase.Idle)
        {
            _refreshDebounceTimer.Stop();
            bool preserveHeadlineIndex = !_resetToFirstHeadlineOnRefresh;
            _resetToFirstHeadlineOnRefresh = false;
            ResetPlaybackCore(preserveHeadlineIndex);
            if (IsLoaded && !_isUnloaded)
                _playbackTimer.Start();
            return;
        }

        _playbackTimer.Stop();
        _refreshDebounceTimer.Stop();
        _refreshDebounceTimer.Start();
    }

    private void OnRefreshDebounceElapsed(object? sender, EventArgs e)
    {
        _refreshDebounceTimer.Stop();
        if (_isUnloaded)
            return;

        bool preserveHeadlineIndex = !_resetToFirstHeadlineOnRefresh;
        _resetToFirstHeadlineOnRefresh = false;
        ResetPlaybackCore(preserveHeadlineIndex);
        if (IsLoaded)
            _playbackTimer.Start();
    }

    private void ClearDisplay()
    {
        if (ActiveHeadlineBlock is null)
            return;

        SetDisplayedHeadlineText(string.Empty, includeCursor: false);
        Canvas.SetLeft(ActiveHeadlineBlock, 0d);
        Canvas.SetTop(ActiveHeadlineBlock, 0d);
    }

    private void RefreshLayoutForCurrentState()
    {
        if (!IsViewportReady())
        {
            return;
        }

        ActiveHeadlineBlock.Width = GetSafeViewportWidth();
        if (_phase == PlaybackPhase.Scrolling)
            Canvas.SetTop(ActiveHeadlineBlock, _currentVerticalOffset);
    }

    private void RestartPlaybackForViewportChange()
    {
        if (!_awaitingViewport || !IsViewportReady())
            return;

        CancelHeadlinePreparation();
        if (string.IsNullOrWhiteSpace(_activeText))
            return;

        // A recovered viewport may have different wrapping; restart this headline rather than
        // preserving stale line breaks or scroll offsets from the invalid layout.
        ClearMeasurementCache();
        _pendingRefresh = false;
        _phase = PlaybackPhase.Idle;
        _lastTracedPhase = PlaybackPhase.Idle;
        _visibleCharacterCount = 0;
        _pauseTicksRemaining = 0;
        _segmentIndex = 0;
        _currentVerticalOffset = 0d;
        _wrappedLines = [];
        _displayTopLine = string.Empty;
        _displayBottomLine = string.Empty;
        _activeHeadlineHeight = 0d;
        ClearDisplay();
    }

    private int GetPauseTicks(double seconds)
        => Math.Max(1, (int)Math.Round(seconds / _playbackTimer.Interval.TotalSeconds));

    private double MeasureHeadlineHeight(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0d;

        Typeface typeface = new(
            ActiveHeadlineBlock.FontFamily,
            ActiveHeadlineBlock.FontStyle,
            ActiveHeadlineBlock.FontWeight,
            ActiveHeadlineBlock.FontStretch);

        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        FormattedText formatted = new(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            ActiveHeadlineBlock.FontSize,
            Brushes.White,
            dpi)
        {
            MaxTextWidth = GetSafeViewportWidth()
        };

        return Math.Ceiling(formatted.Height);
    }

    private int GetSegmentCount()
    {
        if (_wrappedLines.Count == 0)
            return 0;

        return _wrappedLines.Count == 1 ? 1 : _wrappedLines.Count - 1;
    }

    private List<string> BuildWrappedLines(string text)
        => BuildWrappedLines(
            text,
            CreatePreparationContext(),
            _headlineWidthCache,
            _headlineWidthCacheGate,
            CancellationToken.None);

    private static List<string> BuildWrappedLines(
        string text,
        HeadlinePreparationContext context,
        IDictionary<MeasurementCacheKey, double>? measurementCache,
        object? measurementCacheGate,
        CancellationToken cancellationToken)
    {
        List<string> lines = [];
        double widthLimit = context.WidthLimit;
        string normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        string[] logicalLines = normalized.Split('\n', StringSplitOptions.None);
        foreach (string logicalLine in logicalLines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string trimmedLogicalLine = logicalLine.Trim();
            if (string.IsNullOrWhiteSpace(trimmedLogicalLine))
                continue;

            string[] words = trimmedLogicalLine
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (words.Length == 0)
                continue;

            string current = string.Empty;
            foreach (string word in words)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string candidate = string.IsNullOrWhiteSpace(current)
                    ? word
                    : current + " " + word;

                if (MeasureHeadlineWidth(candidate, context, measurementCache, measurementCacheGate) <= widthLimit)
                {
                    current = candidate;
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(current))
                    lines.Add(current);

                current = word;
            }

            if (!string.IsNullOrWhiteSpace(current))
                lines.Add(current);
        }

        return lines;
    }

    private static string FormatHeadline(string text)
    {
        string normalized = (text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return string.Empty;

        normalized = normalized.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        string[] lines = normalized.Split('\n', StringSplitOptions.None);
        IEnumerable<string> cleanedLines = lines
            .Select(line => Regex.Replace(line, @"[\u0000-\u0009\u000B-\u001F\u007F]+", " "))
            .Select(line => Regex.Replace(line, @"[ \t]+", " ").Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.ToUpperInvariant());
        return string.Join(Environment.NewLine, cleanedLines);
    }

    private int GetTypingTargetLength()
    {
        if (_segmentIndex == 0)
            return GetFullSegmentText().Length;

        return _displayBottomLine.Length;
    }

    private string GetFullSegmentText()
    {
        if (string.IsNullOrWhiteSpace(_displayTopLine))
            return _displayBottomLine;

        if (string.IsNullOrWhiteSpace(_displayBottomLine))
            return _displayTopLine;

        return $"{_displayTopLine}{Environment.NewLine}{_displayBottomLine}";
    }

    private string BuildVisibleText(bool includeCursor)
    {
        string visibleText;
        if (_segmentIndex == 0)
        {
            string fullSegmentText = GetFullSegmentText();
            visibleText = fullSegmentText[..Math.Min(_visibleCharacterCount, fullSegmentText.Length)];
        }
        else
        {
            string typedBottom = _displayBottomLine[..Math.Min(_visibleCharacterCount, _displayBottomLine.Length)];
            visibleText = string.IsNullOrWhiteSpace(_displayTopLine)
                ? typedBottom
                : $"{_displayTopLine}{Environment.NewLine}{typedBottom}";
        }

        return includeCursor ? visibleText + TeleprinterCursor : visibleText;
    }

    private void SetDisplayedHeadlineText(string text)
    {
        ActiveHeadlineBlock.Text = text;
    }

    private void SetDisplayedHeadlineText(string text, bool includeCursor)
    {
        ActiveHeadlineBlock.Text = includeCursor ? text + TeleprinterCursor : text;
    }

    private double MeasureHeadlineWidth(string text)
        => MeasureHeadlineWidth(text, CreatePreparationContext(), _headlineWidthCache, _headlineWidthCacheGate);

    private static double MeasureHeadlineWidth(
        string text,
        HeadlinePreparationContext context,
        IDictionary<MeasurementCacheKey, double>? measurementCache,
        object? measurementCacheGate)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0d;

        MeasurementCacheKey key = CreateMeasurementCacheKey(text, context);
        if (measurementCache is not null && measurementCacheGate is not null)
        {
            lock (measurementCacheGate)
            {
                if (measurementCache.TryGetValue(key, out double cachedWidth))
                    return cachedWidth;
            }
        }

        Typeface typeface = new(
            new FontFamily(context.FontFamily),
            context.FontStyle,
            context.FontWeight,
            context.FontStretch);

        FormattedText formatted = new(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            context.FontSize,
            Brushes.White,
            context.PixelsPerDip);

        double width = Math.Ceiling(formatted.WidthIncludingTrailingWhitespace);
        if (measurementCache is not null && measurementCacheGate is not null)
        {
            lock (measurementCacheGate)
            {
                if (measurementCache.Count >= MaxWidthMeasurementCacheEntries)
                    measurementCache.Clear();

                measurementCache[key] = width;
            }
        }
        return width;
    }

    private bool IsViewportReady()
    {
        double width = ViewportHost?.ActualWidth ?? 0d;
        double height = ViewportHost?.ActualHeight ?? 0d;
        return double.IsFinite(width)
            && double.IsFinite(height)
            && width > 0d
            && height > 0d;
    }

    private void PausePlaybackUntilViewportReady()
    {
        if (!_awaitingViewport)
        {
            _awaitingViewport = true;
            ClearDisplay();
        }

        _playbackTimer.Stop();
    }

    private void ResumePlaybackWhenViewportReady()
    {
        if (!IsLoaded || !_awaitingViewport || !IsViewportReady())
            return;

        _awaitingViewport = false;
        _playbackTimer.Start();
    }

    private void RecoverPlaybackWhenViewportReady()
    {
        if (!_awaitingViewport || !IsViewportReady())
            return;

        RestartPlaybackForViewportChange();
        RefreshLayoutForCurrentState();
        ResumePlaybackWhenViewportReady();
    }

    private double GetSafeViewportWidth()
    {
        double width = ViewportHost?.ActualWidth ?? 0d;
        return double.IsFinite(width) && width > 0d ? Math.Max(1d, width) : 1d;
    }

    private HeadlinePreparationContext CreatePreparationContext()
    {
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        return new HeadlinePreparationContext(
            GetSafeViewportWidth(),
            dpi,
            ActiveHeadlineBlock.FontFamily?.Source ?? "Courier New",
            ActiveHeadlineBlock.FontStyle,
            ActiveHeadlineBlock.FontWeight,
            ActiveHeadlineBlock.FontStretch,
            ActiveHeadlineBlock.FontSize);
    }

    private static MeasurementCacheKey CreateMeasurementCacheKey(string text, HeadlinePreparationContext context)
    {
        // DPI/font are part of the key so monitor/theme changes cannot reuse stale measurements.
        return new MeasurementCacheKey(
            text,
            context.PixelsPerDip,
            context.FontFamily,
            context.FontStyle.ToString(),
            context.FontWeight.ToOpenTypeWeight(),
            context.FontStretch.ToString(),
            context.FontSize);
    }

    private void ClearMeasurementCache()
    {
        lock (_headlineWidthCacheGate)
        {
            _headlineWidthCache.Clear();
        }
    }

    private void SetPhase(PlaybackPhase phase)
    {
        _phase = phase;
        if (_lastTracedPhase == phase)
            return;

        _lastTracedPhase = phase;
        TraceLog.InfoState(
            "NewsFlasher",
            "PlaybackPhase",
            [
                new("phase", phase.ToString()),
                new("headline_index", _headlineIndex),
                new("visible_character_count", _visibleCharacterCount),
                new("offset", Math.Round(_currentVerticalOffset, 2))
            ]);
    }

    private enum PlaybackPhase
    {
        Idle,
        Typing,
        PauseBeforeScroll,
        Scrolling,
        PauseAfterScroll,
        PauseBetweenHeadlines,
        AdvanceHeadline
    }

    private readonly record struct MeasurementCacheKey(
        string Text,
        double PixelsPerDip,
        string FontFamily,
        string FontStyle,
        int FontWeight,
        string FontStretch,
        double FontSize);

    private readonly record struct HeadlinePreparationContext(
        double WidthLimit,
        double PixelsPerDip,
        string FontFamily,
        FontStyle FontStyle,
        FontWeight FontWeight,
        FontStretch FontStretch,
        double FontSize);

    private readonly record struct PreparedHeadline(
        string ActiveText,
        IReadOnlyList<string> WrappedLines);
}


