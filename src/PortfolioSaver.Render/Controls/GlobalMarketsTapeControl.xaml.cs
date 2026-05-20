using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using PortfolioSaver.Core.Enums;
using PortfolioSaver.Render.Services;
using PortfolioSaver.Render.ViewModels;

namespace PortfolioSaver.Render.Controls;

public partial class GlobalMarketsTapeControl : UserControl
{
    private const string PinnedExchangeKey = "NewYorkNasdaq";
    private const double CardWidth = 164d;
    private const double PinnedCardWidth = 150d;
    private const double CardHeight = 54d;
    private const double CopySpacing = 14d;
    private const double SequenceLeadInSpacing = 10d;
    private const double SequenceTailSpacing = 10d;
    private const double FlagBadgeWidth = 20d;
    private const double FlagBadgeHeight = 14d;
    private static readonly Dictionary<string, ImageSource> FlagImageCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly TapeAnimationController _animationController = new();
    private FloatingClockViewModel? _clock;
    private bool _metricsQueued;
    private string _contentSignature = string.Empty;
    private int _renderedSideCopyCount;

    public GlobalMarketsTapeControl()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_clock is null)
            SubscribeToClock(DataContext as FloatingClockViewModel);

        _animationController.Attach(TrackPanel);
        _animationController.Start();
        QueueMetricsUpdate();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _animationController.Stop();
        UnsubscribeFromClock(_clock);
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => QueueMetricsUpdate();

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OldValue, e.NewValue))
        {
            UnsubscribeFromClock(e.OldValue as FloatingClockViewModel);
            SubscribeToClock(e.NewValue as FloatingClockViewModel);
        }

        QueueMetricsUpdate();
    }

    private void QueueMetricsUpdate()
    {
        if (_metricsQueued)
            return;

        _metricsQueued = true;
        Dispatcher.BeginInvoke(RefreshMotionMetrics, DispatcherPriority.Loaded);
    }

    private void RefreshMotionMetrics()
    {
        _metricsQueued = false;

        if (DataContext is not FloatingClockViewModel clock)
        {
            TrackPanel.Children.Clear();
            TrackPanel.Width = 0d;
            _contentSignature = string.Empty;
            _renderedSideCopyCount = 0;
            PinnedCardHost.Child = null;
            return;
        }

        RefreshPinnedCard(clock);

        List<ClockCityViewModel> scrollingCities = GetScrollingExchangeCities(clock).ToList();
        if (scrollingCities.Count == 0)
        {
            TrackPanel.Children.Clear();
            TrackPanel.Width = 0d;
            _contentSignature = string.Empty;
            _renderedSideCopyCount = 0;
            _animationController.Stop();
            return;
        }

        UpdateLayout();
        double viewportWidth = ViewportHost.ActualWidth;
        if (viewportWidth <= 0)
            return;

        string signature = BuildMarketSignature(clock);
        double sequenceWidth = MeasureSequenceWidth(clock);
        if (sequenceWidth <= 0)
            return;

        int requiredSideCopies = CalculateSideCopyCount(viewportWidth, sequenceWidth);
        if (!string.Equals(signature, _contentSignature, StringComparison.Ordinal) ||
            requiredSideCopies != _renderedSideCopyCount)
        {
            RebuildTrack(clock, requiredSideCopies, sequenceWidth);
            _contentSignature = signature;
            _renderedSideCopyCount = requiredSideCopies;
        }

        double cycleDistance = sequenceWidth + CopySpacing;
        const double pixelsPerSecond = 30d;
        double anchorOffset = -requiredSideCopies * cycleDistance;
        _animationController.Attach(TrackPanel);
        _animationController.Start();
        _animationController.Update(cycleDistance, pixelsPerSecond, ScrollDirection.Left, anchorOffset);
    }

    private void SubscribeToClock(FloatingClockViewModel? clock)
    {
        _clock = clock;
        _contentSignature = string.Empty;
        _renderedSideCopyCount = 0;
        if (_clock is null)
            return;

        _clock.PropertyChanged += OnClockPropertyChanged;
        _clock.Cities.CollectionChanged += OnCitiesCollectionChanged;
        foreach (ClockCityViewModel city in _clock.Cities)
            city.PropertyChanged += OnCityPropertyChanged;
    }

    private void UnsubscribeFromClock(FloatingClockViewModel? clock)
    {
        if (clock is null)
            return;

        clock.PropertyChanged -= OnClockPropertyChanged;
        clock.Cities.CollectionChanged -= OnCitiesCollectionChanged;
        foreach (ClockCityViewModel city in clock.Cities)
            city.PropertyChanged -= OnCityPropertyChanged;

        _contentSignature = string.Empty;
        _renderedSideCopyCount = 0;
        if (ReferenceEquals(_clock, clock))
            _clock = null;
    }

    private void OnClockPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FloatingClockViewModel.Title) or nameof(FloatingClockViewModel.Subtitle))
            QueueMetricsUpdate();
    }

    private void OnCitiesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (ClockCityViewModel city in e.OldItems)
                city.PropertyChanged -= OnCityPropertyChanged;
        }

        if (e.NewItems is not null)
        {
            foreach (ClockCityViewModel city in e.NewItems)
                city.PropertyChanged += OnCityPropertyChanged;
        }

        QueueMetricsUpdate();
    }

    private void OnCityPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ClockCityViewModel.Label) or
            nameof(ClockCityViewModel.ExchangeName) or
            nameof(ClockCityViewModel.ShowExchangeDetails) or
            nameof(ClockCityViewModel.FlagCode))
        {
            QueueMetricsUpdate();
        }
    }

    private double MeasureSequenceWidth(FloatingClockViewModel clock)
    {
        FrameworkElement sequence = BuildSequencePanel(clock);
        sequence.Measure(new Size(double.PositiveInfinity, Math.Max(1d, ViewportHost.ActualHeight)));
        return Math.Max(sequence.ActualWidth, sequence.DesiredSize.Width);
    }

    private void RebuildTrack(FloatingClockViewModel clock, int sideCopies, double sequenceWidth)
    {
        TrackPanel.Children.Clear();
        double sequenceSpan = sequenceWidth + CopySpacing;
        int totalCopies = sideCopies * 2 + 1;

        for (int index = 0; index < totalCopies; index++)
        {
            FrameworkElement sequence = BuildSequencePanel(clock);
            Canvas.SetLeft(sequence, index * sequenceSpan);
            Canvas.SetTop(sequence, 0d);
            TrackPanel.Children.Add(sequence);
        }

        TrackPanel.Width = totalCopies * sequenceSpan;
        TrackPanel.Height = Math.Max(1d, ViewportHost.ActualHeight);
    }

    private static int CalculateSideCopyCount(double viewportWidth, double contentWidth)
    {
        double sequenceSpan = Math.Max(1d, contentWidth + CopySpacing);
        return Math.Max(2, (int)Math.Ceiling(viewportWidth / sequenceSpan) + 2);
    }

    private static string BuildMarketSignature(FloatingClockViewModel clock)
        => string.Join("|", GetScrollingExchangeCities(clock).Select(city =>
            $"{city.Key}:{city.Label}:{city.ExchangeName}:{city.ExchangeSymbol}:{city.FlagCode}"));

    private void RefreshPinnedCard(FloatingClockViewModel clock)
    {
        ClockCityViewModel? pinned = GetPinnedExchangeCity(clock);
        PinnedCardHost.Child = pinned is null ? null : BuildMarketCard(pinned, isPinned: true);
    }

    private static FrameworkElement BuildSequencePanel(FloatingClockViewModel clock)
    {
        StackPanel panel = new()
        {
            Orientation = Orientation.Horizontal,
            Height = 66,
            VerticalAlignment = VerticalAlignment.Center
        };

        panel.Children.Add(new Border
        {
            Width = SequenceLeadInSpacing,
            Background = Brushes.Transparent
        });

        List<ClockCityViewModel> cities = GetScrollingExchangeCities(clock).ToList();
        for (int index = 0; index < cities.Count; index++)
        {
            panel.Children.Add(BuildMarketCard(cities[index], isPinned: false));
            panel.Children.Add(BuildDelimiter());
        }

        panel.Children.Add(new Border
        {
            Width = SequenceTailSpacing,
            Background = Brushes.Transparent
        });

        return panel;
    }

    private static TextBlock BuildDelimiter()
        => new()
        {
            Text = "|",
            Foreground = new SolidColorBrush(Color.FromRgb(92, 122, 142)),
            FontFamily = new FontFamily("Bahnschrift SemiCondensed"),
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(10, 0, 14, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

    private static FrameworkElement BuildMarketCard(ClockCityViewModel city, bool isPinned)
    {
        Border border = new()
        {
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(isPinned ? 6 : 5, 3, isPinned ? 6 : 5, 3),
            Width = isPinned ? PinnedCardWidth : CardWidth,
            Height = CardHeight,
            DataContext = city,
            Margin = isPinned ? new Thickness(0, 0, 6, 0) : default
        };

        Grid grid = new();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        StackPanel left = new() { Orientation = Orientation.Vertical, Margin = new Thickness(0, 0, 5, 0), Width = isPinned ? 56 : 58 };
        StackPanel locationHeader = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 1) };
        locationHeader.Children.Add(BuildFlagBadge(city));
        TextBlock label = new()
        {
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Bahnschrift SemiCondensed"),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            MaxWidth = 40,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        BindingOperations.SetBinding(label, TextBlock.TextProperty, new Binding(nameof(ClockCityViewModel.Label)));
        locationHeader.Children.Add(label);
        TextBlock time = new()
        {
            Foreground = new SolidColorBrush(Color.FromRgb(218, 238, 248)),
            FontFamily = new FontFamily("Cascadia Mono,Consolas,Lucida Console"),
            FontSize = 7.5,
            FontWeight = FontWeights.SemiBold
        };
        BindingOperations.SetBinding(time, TextBlock.TextProperty, new Binding(nameof(ClockCityViewModel.TimeText)));
        TextBlock weather = new()
        {
            Foreground = new SolidColorBrush(Color.FromRgb(205, 223, 236)),
            FontFamily = new FontFamily("Segoe UI Emoji"),
            FontSize = 7.5,
            Margin = new Thickness(0, 2, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 54
        };
        MultiBinding weatherBinding = new() { StringFormat = "{0} {1}" };
        weatherBinding.Bindings.Add(new Binding(nameof(ClockCityViewModel.WeatherGlyph)));
        weatherBinding.Bindings.Add(new Binding(nameof(ClockCityViewModel.WeatherText)));
        BindingOperations.SetBinding(weather, TextBlock.TextProperty, weatherBinding);
        left.Children.Add(locationHeader);
        left.Children.Add(time);
        left.Children.Add(weather);
        Grid.SetColumn(left, 0);
        grid.Children.Add(left);

        StackPanel center = new() { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center, Width = isPinned ? 48 : 50 };
        StackPanel exchangeHeader = new() { Orientation = Orientation.Horizontal };
        TextBlock exchange = new()
        {
            Foreground = new SolidColorBrush(Color.FromRgb(178, 206, 224)),
            FontFamily = new FontFamily("Bahnschrift SemiCondensed"),
            FontSize = 8,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = isPinned ? 48 : 50
        };
        BindingOperations.SetBinding(exchange, TextBlock.TextProperty, new Binding(nameof(ClockCityViewModel.ExchangeName)));
        exchangeHeader.Children.Add(exchange);
        TextBlock marketStatus = new()
        {
            FontFamily = new FontFamily("Bahnschrift SemiCondensed"),
            FontSize = 6.5,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 1, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 54
        };
        BindingOperations.SetBinding(marketStatus, TextBlock.TextProperty, new Binding(nameof(ClockCityViewModel.MarketStatusText)));
        BindingOperations.SetBinding(marketStatus, TextBlock.ForegroundProperty, new Binding(nameof(ClockCityViewModel.MarketStatusForeground)));
        Grid sparkline = new() { Height = 14, Width = isPinned ? 48 : 50, Margin = new Thickness(0, 2, 0, 0) };
        sparkline.Children.Add(new Rectangle
        {
            Height = 1,
            Fill = new SolidColorBrush(Color.FromArgb(0x36, 0xAF, 0xC6, 0xD8)),
            VerticalAlignment = VerticalAlignment.Bottom
        });
        sparkline.Children.Add(new Rectangle
        {
            Width = 1,
            Fill = new SolidColorBrush(Color.FromArgb(0x42, 0xAF, 0xC6, 0xD8)),
            HorizontalAlignment = HorizontalAlignment.Left
        });
        sparkline.Children.Add(new Rectangle
        {
            Width = 1,
            Fill = new SolidColorBrush(Color.FromArgb(0x42, 0xAF, 0xC6, 0xD8)),
            HorizontalAlignment = HorizontalAlignment.Right
        });
        Polyline line = new() { StrokeThickness = 1.6, StrokeLineJoin = PenLineJoin.Round };
        BindingOperations.SetBinding(line, Polyline.PointsProperty, new Binding(nameof(ClockCityViewModel.MiniGraphPoints)));
        BindingOperations.SetBinding(line, Shape.StrokeProperty, new Binding(nameof(ClockCityViewModel.MiniGraphStroke)));
        sparkline.Children.Add(line);
        center.Children.Add(exchangeHeader);
        center.Children.Add(marketStatus);
        center.Children.Add(sparkline);
        Grid axis = new() { Width = isPinned ? 48 : 50, Height = 8, Margin = new Thickness(0, 1, 0, 0) };
        axis.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        axis.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        axis.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        TextBlock leftAxis = new()
        {
            Text = "5D",
            Foreground = new SolidColorBrush(Color.FromRgb(120, 151, 170)),
            FontFamily = new FontFamily("Cascadia Mono,Consolas,Lucida Console"),
            FontSize = 6
        };
        TextBlock rightAxis = new()
        {
            Text = "now",
            Foreground = new SolidColorBrush(Color.FromRgb(120, 151, 170)),
            FontFamily = new FontFamily("Cascadia Mono,Consolas,Lucida Console"),
            FontSize = 6,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetColumn(rightAxis, 2);
        axis.Children.Add(leftAxis);
        axis.Children.Add(rightAxis);
        center.Children.Add(axis);
        Grid.SetColumn(center, 1);
        grid.Children.Add(center);

        StackPanel right = new() { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(3, 0, 0, 0), Width = isPinned ? 40 : 44 };
        TextBlock value = new()
        {
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Cascadia Mono,Consolas,Lucida Console"),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Right,
            MinWidth = isPinned ? 38 : 40
        };
        BindingOperations.SetBinding(value, TextBlock.TextProperty, new Binding(nameof(ClockCityViewModel.IndexValueText)));
        TextBlock change = new()
        {
            FontFamily = new FontFamily("Cascadia Mono,Consolas,Lucida Console"),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Right,
            MinWidth = isPinned ? 38 : 40
        };
        BindingOperations.SetBinding(change, TextBlock.TextProperty, new Binding(nameof(ClockCityViewModel.IndexChangeText)));
        BindingOperations.SetBinding(change, TextBlock.ForegroundProperty, new Binding(nameof(ClockCityViewModel.IndexChangeForeground)));
        right.Children.Add(value);
        right.Children.Add(change);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        border.Child = grid;
        return border;
    }

    private static FrameworkElement BuildFlagBadge(ClockCityViewModel city)
    {
        Border host = new()
        {
            Width = FlagBadgeWidth,
            Height = FlagBadgeHeight,
            CornerRadius = new CornerRadius(2),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(0.5),
            Margin = new Thickness(0, 0, 5, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ClipToBounds = true,
            Padding = new Thickness(0)
        };

        Image image = new()
        {
            Width = FlagBadgeWidth,
            Height = FlagBadgeHeight,
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.Both,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
        };
        ImageSource? source = GetFlagImageSource(city.FlagCode);
        if (source is not null)
        {
            image.Source = source;
            host.Child = image;
            return host;
        }

        host.Background = new SolidColorBrush(Color.FromRgb(0x46, 0x67, 0x85));
        return host;
    }

    private static ImageSource? GetFlagImageSource(string? flagCode)
    {
        if (string.IsNullOrWhiteSpace(flagCode))
            return null;

        if (FlagImageCache.TryGetValue(flagCode, out ImageSource? cached))
            return cached;

        try
        {
            BitmapImage bitmap = new();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(
                $"pack://application:,,,/PortfolioSaver.Render;component/Assets/Flags/{flagCode.ToLowerInvariant()}.png",
                UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            FlagImageCache[flagCode] = bitmap;
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static ClockCityViewModel? GetPinnedExchangeCity(FloatingClockViewModel clock)
        => clock.Cities.FirstOrDefault(city =>
            city.ShowExchangeDetails &&
            string.Equals(city.Key, PinnedExchangeKey, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<ClockCityViewModel> GetScrollingExchangeCities(FloatingClockViewModel clock)
        => clock.Cities.Where(city =>
            city.ShowExchangeDetails &&
            !string.Equals(city.Key, PinnedExchangeKey, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<ClockCityViewModel> GetExchangeCities(FloatingClockViewModel clock)
        => clock.Cities.Where(city => city.ShowExchangeDetails);
}
