using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
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
    private const int TypewriterCharactersPerTick = 2;
    private const int MaxWidthMeasurementCacheEntries = 256;
    private const string TeleprinterCursor = " █";
    private readonly DispatcherTimer _playbackTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
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
    private IReadOnlyList<string> _wrappedLines = [];
    private readonly Dictionary<MeasurementCacheKey, double> _headlineWidthCache = [];
    private string _displayTopLine = string.Empty;
    private string _displayBottomLine = string.Empty;
    private string _activeText = string.Empty;
    private Brush _activeForeground = Brushes.WhiteSmoke;
    private bool _awaitingViewport;
    private bool _layoutUpdatedSubscribed;

    public NewsFlasherControl()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += (_, _) =>
        {
            ClearMeasurementCache();
            RefreshLayoutForCurrentState();
            RecoverPlaybackWhenViewportReady();
        };
        SubscribeToLayoutUpdated();
        DataContextChanged += OnDataContextChanged;
        _playbackTimer.Tick += OnPlaybackTick;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SubscribeToLayoutUpdated();
        _awaitingViewport = false;
        if (_flasher is null)
            SubscribeToFlasher(DataContext as NewsFlasherViewModel);

        ResetPlayback();
        _playbackTimer.Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _playbackTimer.Stop();
        _awaitingViewport = false;
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
            RequestRefresh();
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

        RequestRefresh();
    }

    private void OnHeadlinePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NewsHeadlineViewModel.Text) or nameof(NewsHeadlineViewModel.Foreground))
            RequestRefresh();
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
            PrepareHeadline(headlines[_headlineIndex % headlines.Count]);

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

        _activeText = FormatHeadline(headline.Text);
        ClearMeasurementCache();
        _wrappedLines = BuildWrappedLines(_activeText);
        _segmentIndex = 0;
        _activeForeground = headline.Foreground;
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

    private void ResetPlayback()
    {
        _pendingRefresh = false;
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
        ClearMeasurementCache();
        ClearDisplay();
    }

    private void RequestRefresh()
    {
        if (_phase == PlaybackPhase.Idle)
        {
            ResetPlayback();
            return;
        }

        _pendingRefresh = true;
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
        if (!_awaitingViewport || !IsViewportReady() || string.IsNullOrWhiteSpace(_activeText))
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
    {
        List<string> lines = [];
        double widthLimit = GetSafeViewportWidth();
        string normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        string[] logicalLines = normalized.Split('\n', StringSplitOptions.None);
        foreach (string logicalLine in logicalLines)
        {
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
                string candidate = string.IsNullOrWhiteSpace(current)
                    ? word
                    : current + " " + word;

                if (MeasureHeadlineWidth(candidate) <= widthLimit)
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
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0d;

        MeasurementCacheKey key = CreateMeasurementCacheKey(text);
        if (_headlineWidthCache.TryGetValue(key, out double cachedWidth))
            return cachedWidth;

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
            dpi);

        double width = Math.Ceiling(formatted.WidthIncludingTrailingWhitespace);
        if (_headlineWidthCache.Count >= MaxWidthMeasurementCacheEntries)
            _headlineWidthCache.Clear();

        _headlineWidthCache[key] = width;
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

    private MeasurementCacheKey CreateMeasurementCacheKey(string text)
    {
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        // DPI/font are part of the key so monitor/theme changes cannot reuse stale measurements.
        return new MeasurementCacheKey(
            text,
            dpi,
            ActiveHeadlineBlock.FontFamily?.Source ?? string.Empty,
            ActiveHeadlineBlock.FontStyle.ToString(),
            ActiveHeadlineBlock.FontWeight.ToOpenTypeWeight(),
            ActiveHeadlineBlock.FontStretch.ToString(),
            ActiveHeadlineBlock.FontSize);
    }

    private void ClearMeasurementCache()
    {
        _headlineWidthCache.Clear();
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
}
