using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PortfolioSaver.Render.ViewModels;

namespace PortfolioSaver.Render.Controls;

public partial class NewsFlasherControl : UserControl
{
    private const double DefaultPauseSeconds = 1.8d;
    private const double DefaultPreScrollPauseSeconds = 0.9d;
    private const double TelegraphScrollPixelsPerSecond = 48d;
    private const int TypewriterCharactersPerTick = 1;
    private const int ClearCharactersPerTick = 2;
    private const string TeleprinterCursor = " █";
    private readonly DispatcherTimer _playbackTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private NewsFlasherViewModel? _flasher;
    private int _headlineIndex;
    private int _visibleCharacterCount;
    private int _pauseTicksRemaining;
    private double _currentOffset;
    private double _activeHeadlineWidth;
    private PlaybackPhase _phase = PlaybackPhase.Idle;
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
        if (e.PropertyName is nameof(NewsFlasherViewModel.Speed) or nameof(NewsFlasherViewModel.MarqueeText))
            ResetPlayback();
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

        ResetPlayback();
    }

    private void OnHeadlinePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NewsHeadlineViewModel.Text) or nameof(NewsHeadlineViewModel.Foreground))
            ResetPlayback();
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
                StepPause(PlaybackPhase.Scrolling);
                break;
            case PlaybackPhase.Scrolling:
                StepScrolling();
                break;
            case PlaybackPhase.PauseAfterItem:
                StepPause(PlaybackPhase.Clearing);
                break;
            case PlaybackPhase.Clearing:
                StepClearing(headlines.Count);
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
        _activeForeground = headline.Foreground;
        _visibleCharacterCount = 0;
        _pauseTicksRemaining = 0;
        _currentOffset = 0d;
        _phase = PlaybackPhase.Typing;
        ActiveHeadlineBlock.Foreground = _activeForeground;
        Canvas.SetLeft(ActiveHeadlineBlock, 0d);
        SetDisplayedHeadlineText(string.Empty, includeCursor: true);
        _activeHeadlineWidth = MeasureHeadlineWidth(_activeText);
    }

    private void StepTyping()
    {
        if (string.IsNullOrWhiteSpace(_activeText))
        {
            _phase = PlaybackPhase.Clearing;
            return;
        }

        _visibleCharacterCount = Math.Min(_activeText.Length, _visibleCharacterCount + GetCharactersPerTick());
        SetDisplayedHeadlineText(_activeText[.._visibleCharacterCount], includeCursor: _visibleCharacterCount < _activeText.Length);
        ActiveHeadlineBlock.Foreground = _activeForeground;
        Canvas.SetLeft(ActiveHeadlineBlock, 0d);

        if (_visibleCharacterCount < _activeText.Length)
            return;

        bool needsScroll = _activeHeadlineWidth > Math.Max(1d, ViewportHost.ActualWidth);
        _phase = needsScroll ? PlaybackPhase.PauseBeforeScroll : PlaybackPhase.PauseAfterItem;
        _pauseTicksRemaining = GetPauseTicks(needsScroll ? DefaultPreScrollPauseSeconds : DefaultPauseSeconds);
    }

    private void StepScrolling()
    {
        double viewportWidth = Math.Max(1d, ViewportHost.ActualWidth);
        double pixelsPerTick = TelegraphScrollPixelsPerSecond * (_playbackTimer.Interval.TotalSeconds * Math.Max(0.5d, _flasher?.Speed ?? 1d));
        double targetOffset = viewportWidth - _activeHeadlineWidth;
        _currentOffset = Math.Max(targetOffset, _currentOffset - pixelsPerTick);
        SetDisplayedHeadlineText(_activeText, includeCursor: false);
        Canvas.SetLeft(ActiveHeadlineBlock, _currentOffset);

        if (_currentOffset <= targetOffset + 0.1d)
        {
            _phase = PlaybackPhase.PauseAfterItem;
            _pauseTicksRemaining = GetPauseTicks(DefaultPauseSeconds);
        }
    }

    private void StepPause(PlaybackPhase nextPhase)
    {
        if (_pauseTicksRemaining > 0)
        {
            SetDisplayedHeadlineText(_activeText, includeCursor: true);
            _pauseTicksRemaining--;
            return;
        }

        _phase = nextPhase;
    }

    private void StepClearing(int headlineCount)
    {
        if (_visibleCharacterCount > 0)
        {
            _visibleCharacterCount = Math.Max(0, _visibleCharacterCount - ClearCharactersPerTick);
            SetDisplayedHeadlineText(_activeText[.._visibleCharacterCount], includeCursor: false);
            Canvas.SetLeft(ActiveHeadlineBlock, 0d);
            return;
        }

        _headlineIndex = (_headlineIndex + 1) % Math.Max(1, headlineCount);
        _phase = PlaybackPhase.Idle;
    }

    private void ResetPlayback()
    {
        _headlineIndex = 0;
        _phase = PlaybackPhase.Idle;
        _visibleCharacterCount = 0;
        _pauseTicksRemaining = 0;
        _currentOffset = 0d;
        _activeText = string.Empty;
        _activeHeadlineWidth = 0d;
        ClearDisplay();
    }

    private void ClearDisplay()
    {
        SetDisplayedHeadlineText(string.Empty, includeCursor: false);
        Canvas.SetLeft(ActiveHeadlineBlock, 0d);
    }

    private void RefreshLayoutForCurrentState()
    {
        if (_phase == PlaybackPhase.Scrolling)
            Canvas.SetLeft(ActiveHeadlineBlock, _currentOffset);
    }

    private int GetCharactersPerTick()
    {
        return TypewriterCharactersPerTick;
    }

    private int GetPauseTicks(double seconds)
        => Math.Max(1, (int)Math.Round(seconds / _playbackTimer.Interval.TotalSeconds));

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

    private static string FormatHeadline(string text)
    {
        string normalized = (text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return string.Empty;

        normalized = Regex.Replace(normalized, @"[\u0000-\u001F\u007F]+", " ");
        normalized = Regex.Replace(normalized, @"\s+", " ");
        string upper = normalized.ToUpperInvariant();
        return upper.EndsWith(" STOP", StringComparison.Ordinal) ? upper : upper + " STOP";
    }

    private void SetDisplayedHeadlineText(string text, bool includeCursor)
    {
        ActiveHeadlineBlock.Text = includeCursor
            ? text + TeleprinterCursor
            : text;
    }

    private enum PlaybackPhase
    {
        Idle,
        Typing,
        PauseBeforeScroll,
        Scrolling,
        PauseAfterItem,
        Clearing
    }
}
