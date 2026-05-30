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
    private const double TelegraphVerticalScrollPixelsPerSecond = 42d;
    private const int TypewriterCharactersPerTick = 2;
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
    private IReadOnlyList<string> _activeSegments = [];
    private string _activeText = string.Empty;
    private Brush _activeForeground = Brushes.WhiteSmoke;

    public NewsFlasherControl()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += (_, _) => RefreshLayoutForCurrentState();
        DataContextChanged += OnDataContextChanged;
        _playbackTimer.Tick += OnPlaybackTick;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_flasher is null)
            SubscribeToFlasher(DataContext as NewsFlasherViewModel);

        ResetPlayback();
        _playbackTimer.Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _playbackTimer.Stop();
        UnsubscribeFromFlasher(_flasher);
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
        _activeText = FormatHeadline(headline.Text);
        _activeSegments = BuildDisplaySegments(_activeText);
        _segmentIndex = 0;
        _activeForeground = headline.Foreground;
        TraceLog.InfoState(
            "NewsFlasher",
            "PrepareHeadline",
            [
                new("headline_index", _headlineIndex),
                new("text_length", _activeText.Length),
                new("segment_count", _activeSegments.Count),
                new("viewport_width", Math.Round(ViewportHost.ActualWidth, 1))
            ]);
        PrepareCurrentSegment();
    }

    private void PrepareCurrentSegment()
    {
        _activeText = _activeSegments.Count > 0
            ? _activeSegments[Math.Clamp(_segmentIndex, 0, _activeSegments.Count - 1)]
            : string.Empty;
        _visibleCharacterCount = 0;
        _pauseTicksRemaining = 0;
        _currentVerticalOffset = 0d;
        SetPhase(PlaybackPhase.Typing);
        ActiveHeadlineBlock.Foreground = _activeForeground;
        ActiveHeadlineBlock.Width = Math.Max(1d, ViewportHost.ActualWidth);
        Canvas.SetLeft(ActiveHeadlineBlock, 0d);
        Canvas.SetTop(ActiveHeadlineBlock, 0d);
        SetDisplayedHeadlineText(string.Empty, includeCursor: true);
        _activeHeadlineHeight = MeasureHeadlineHeight(_activeText);
        TraceLog.InfoState(
            "NewsFlasher",
            "PrepareSegment",
            [
                new("headline_index", _headlineIndex),
                new("segment_index", _segmentIndex),
                new("segment_length", _activeText.Length),
                new("measured_height", _activeHeadlineHeight)
            ]);
    }

    private void StepTyping()
    {
        if (string.IsNullOrWhiteSpace(_activeText))
        {
            SetPhase(PlaybackPhase.AdvanceHeadline);
            return;
        }

        _visibleCharacterCount = Math.Min(_activeText.Length, _visibleCharacterCount + TypewriterCharactersPerTick);
        SetDisplayedHeadlineText(_activeText[.._visibleCharacterCount], includeCursor: _visibleCharacterCount < _activeText.Length);
        ActiveHeadlineBlock.Foreground = _activeForeground;
        ActiveHeadlineBlock.Width = Math.Max(1d, ViewportHost.ActualWidth);
        Canvas.SetLeft(ActiveHeadlineBlock, 0d);
        Canvas.SetTop(ActiveHeadlineBlock, 0d);

        if (_visibleCharacterCount < _activeText.Length)
            return;

        _activeHeadlineHeight = MeasureHeadlineHeight(_activeText);
        SetPhase(PlaybackPhase.PauseBeforeScroll);
        _pauseTicksRemaining = GetPauseTicks(DefaultRevealPauseSeconds);
    }

    private void StepScrolling()
    {
        double pixelsPerTick = TelegraphVerticalScrollPixelsPerSecond * (_playbackTimer.Interval.TotalSeconds * Math.Max(0.7d, _flasher?.Speed ?? 1d));
        double lineShift = _activeText.Contains(Environment.NewLine, StringComparison.Ordinal)
            ? VisibleLineHeight
            : 0d;
        double targetOffset = -lineShift;
        _currentVerticalOffset = Math.Max(targetOffset, _currentVerticalOffset - pixelsPerTick);
        SetDisplayedHeadlineText(_activeText, includeCursor: false);
        Canvas.SetLeft(ActiveHeadlineBlock, 0d);
        Canvas.SetTop(ActiveHeadlineBlock, _currentVerticalOffset);

        if (_currentVerticalOffset <= targetOffset + 0.1d)
        {
            SetPhase(PlaybackPhase.PauseAfterScroll);
            _pauseTicksRemaining = GetPauseTicks(DefaultPostScrollPauseSeconds);
        }
    }

    private void StepPause(PlaybackPhase nextPhase, bool includeCursor)
    {
        if (_pauseTicksRemaining > 0)
        {
            SetDisplayedHeadlineText(_activeText, includeCursor);
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

        if (_segmentIndex + 1 < _activeSegments.Count)
        {
            _segmentIndex++;
            PrepareCurrentSegment();
            return;
        }

        _headlineIndex = (_headlineIndex + 1) % Math.Max(1, headlineCount);
        SetPhase(PlaybackPhase.Idle);
    }

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
        _activeSegments = [];
        _activeText = string.Empty;
        _activeHeadlineHeight = 0d;
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
        SetDisplayedHeadlineText(string.Empty, includeCursor: false);
        Canvas.SetLeft(ActiveHeadlineBlock, 0d);
        Canvas.SetTop(ActiveHeadlineBlock, 0d);
    }

    private void RefreshLayoutForCurrentState()
    {
        ActiveHeadlineBlock.Width = Math.Max(1d, ViewportHost.ActualWidth);
        if (_phase == PlaybackPhase.Scrolling)
            Canvas.SetTop(ActiveHeadlineBlock, _currentVerticalOffset);
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
            MaxTextWidth = Math.Max(1d, ViewportHost.ActualWidth)
        };

        return Math.Ceiling(formatted.Height);
    }

    private List<string> BuildDisplaySegments(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        List<string> wrappedLines = BuildWrappedLines(text);
        if (wrappedLines.Count == 0)
            return [];

        if (wrappedLines.Count == 1)
            return [wrappedLines[0]];

        List<string> segments = [];
        for (int index = 0; index < wrappedLines.Count - 1; index++)
            segments.Add($"{wrappedLines[index]}{Environment.NewLine}{wrappedLines[index + 1]}");

        return segments;
    }

    private List<string> BuildWrappedLines(string text)
    {
        string[] words = text
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
            return [];

        List<string> lines = [];
        string current = string.Empty;
        foreach (string word in words)
        {
            string candidate = string.IsNullOrWhiteSpace(current)
                ? word
                : current + " " + word;

            if (MeasureHeadlineWidth(candidate) <= Math.Max(1d, ViewportHost.ActualWidth))
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

        return lines;
    }

    private static string FormatHeadline(string text)
    {
        string normalized = (text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return string.Empty;

        normalized = Regex.Replace(normalized, @"[\u0000-\u001F\u007F]+", " ");
        normalized = Regex.Replace(normalized, @"\s+", " ");
        return normalized.ToUpperInvariant();
    }

    private void SetDisplayedHeadlineText(string text, bool includeCursor)
    {
        ActiveHeadlineBlock.Text = includeCursor
            ? text + TeleprinterCursor
            : text;
    }

    private double MeasureHeadlineWidth(string text)
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
            dpi);

        return Math.Ceiling(formatted.WidthIncludingTrailingWhitespace);
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
        AdvanceHeadline
    }
}
