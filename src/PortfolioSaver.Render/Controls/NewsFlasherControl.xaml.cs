using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using PortfolioSaver.Core.Enums;
using PortfolioSaver.Render.Services;
using PortfolioSaver.Render.ViewModels;

namespace PortfolioSaver.Render.Controls;

public partial class NewsFlasherControl : UserControl
{
    private const double CopySpacing = 24d;
    private readonly TapeAnimationController _animationController = new();
    private bool _metricsQueued;
    private NewsFlasherViewModel? _flasher;
    private string _contentSignature = string.Empty;
    private int _renderedSideCopyCount;

    public NewsFlasherControl()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_flasher is null)
            SubscribeToFlasher(DataContext as NewsFlasherViewModel);

        _animationController.Attach(TrackPanel);
        _animationController.Start();
        QueueMetricsUpdate();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _animationController.Stop();
        UnsubscribeFromFlasher(_flasher);
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => QueueMetricsUpdate();

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OldValue, e.NewValue))
        {
            UnsubscribeFromFlasher(e.OldValue as NewsFlasherViewModel);
            SubscribeToFlasher(e.NewValue as NewsFlasherViewModel);
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

        if (DataContext is not NewsFlasherViewModel flasher || !flasher.Headlines.Any())
        {
            TrackPanel.Children.Clear();
            TrackPanel.Width = 0d;
            _contentSignature = string.Empty;
            _renderedSideCopyCount = 0;
            return;
        }

        UpdateLayout();
        double viewportWidth = ViewportHost.ActualWidth;
        if (viewportWidth <= 0)
            return;

        string signature = BuildHeadlineSignature(flasher);
        double sequenceWidth = MeasureSequenceWidth(flasher);
        if (sequenceWidth <= 0)
            return;

        int requiredSideCopies = CalculateSideCopyCount(viewportWidth, sequenceWidth);
        if (!string.Equals(signature, _contentSignature, StringComparison.Ordinal) || requiredSideCopies != _renderedSideCopyCount)
        {
            RebuildTrack(flasher, requiredSideCopies, sequenceWidth);
            _contentSignature = signature;
            _renderedSideCopyCount = requiredSideCopies;
        }

        double cycleDistance = sequenceWidth + CopySpacing;
        double pixelsPerSecond = Math.Max(15d, 210.9375d * Math.Max(0.1d, flasher.Speed));
        double anchorOffset = -requiredSideCopies * cycleDistance;
        _animationController.Attach(TrackPanel);
        _animationController.Update(cycleDistance, pixelsPerSecond, ScrollDirection.Left, anchorOffset);
    }

    private void SubscribeToFlasher(NewsFlasherViewModel? flasher)
    {
        _flasher = flasher;
        _contentSignature = string.Empty;
        _renderedSideCopyCount = 0;
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
        _contentSignature = string.Empty;
        _renderedSideCopyCount = 0;
        if (ReferenceEquals(_flasher, flasher))
            _flasher = null;
    }

    private void OnFlasherPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NewsFlasherViewModel.Speed) or nameof(NewsFlasherViewModel.MarqueeText))
            QueueMetricsUpdate();
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

        QueueMetricsUpdate();
    }

    private void OnHeadlinePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NewsHeadlineViewModel.Text) or nameof(NewsHeadlineViewModel.Foreground))
            QueueMetricsUpdate();
    }

    private double MeasureSequenceWidth(NewsFlasherViewModel flasher)
    {
        FrameworkElement sequence = BuildSequencePanel(flasher);
        sequence.Measure(new Size(double.PositiveInfinity, Math.Max(1d, ViewportHost.ActualHeight)));
        return Math.Max(sequence.ActualWidth, sequence.DesiredSize.Width);
    }

    private void RebuildTrack(NewsFlasherViewModel flasher, int sideCopies, double sequenceWidth)
    {
        TrackPanel.Children.Clear();
        double sequenceSpan = sequenceWidth + CopySpacing;
        int totalCopies = sideCopies * 2 + 1;

        for (int index = 0; index < totalCopies; index++)
        {
            FrameworkElement sequence = BuildSequencePanel(flasher);
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

    private static string BuildHeadlineSignature(NewsFlasherViewModel flasher)
        => string.Join("|", flasher.Headlines.Select(headline => headline.Text));

    private static FrameworkElement BuildSequencePanel(NewsFlasherViewModel flasher)
    {
        StackPanel panel = new()
        {
            Orientation = Orientation.Horizontal,
            Height = 38,
            VerticalAlignment = VerticalAlignment.Center
        };

        foreach (NewsHeadlineViewModel headline in flasher.Headlines.Where(item => !string.IsNullOrWhiteSpace(item.Text)))
        {
            panel.Children.Add(BuildHeadlineBlock(headline));
            panel.Children.Add(new TextBlock
            {
                Text = "|",
                Foreground = new SolidColorBrush(Color.FromRgb(196, 64, 68)),
                FontFamily = new FontFamily("Bahnschrift SemiCondensed"),
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 16, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        return panel;
    }

    private static TextBlock BuildHeadlineBlock(NewsHeadlineViewModel headline)
    {
        TextBlock block = new()
        {
            FontFamily = new FontFamily("Bahnschrift SemiCondensed"),
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 18, 0),
            TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center
        };

        BindingOperations.SetBinding(block, TextBlock.TextProperty, new Binding(nameof(NewsHeadlineViewModel.Text)) { Source = headline });
        BindingOperations.SetBinding(block, TextBlock.ForegroundProperty, new Binding(nameof(NewsHeadlineViewModel.Foreground)) { Source = headline });
        return block;
    }
}


