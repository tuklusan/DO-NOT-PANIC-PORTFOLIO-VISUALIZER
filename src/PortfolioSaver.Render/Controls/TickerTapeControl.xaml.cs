// ============================================================================
// Copyright (c) 2026 Supratim Sanyal of SANYALnet Labs.
// Proprietary rights reserved except as expressly licensed herein.
//
// DO NOT PANIC PORTFOLIO VIEWER
// This software and its derivatives are licensed for STRICTLY NON-COMMERCIAL,
// personal, educational, or hobbyist use only. Commercial exploitation,
// corporate internal operations, or AI model training are strictly forbidden.
//
// ATTRIBUTION & DEPENDENCIES: This application incorporates the YFinance library,
// which is licensed under the Apache License, Version 2.0. A copy of the Apache
// License is provided within the distribution environment.
//
// FINANCIAL DISCLAIMER: This software is a passive visualization tool only.
// It does not provide financial, investment, legal, or tax advice. All data
// calculation and scraping outputs are provided 'AS IS' with zero guarantee
// of real-time accuracy or upstream availability.
//
// This file is subject to the terms and conditions defined in the LICENSE
// file located in the root directory of this source code repository.
// Removal or modification of this legal notice constitutes copyright infringement.
// ============================================================================
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using PortfolioSaver.Render.ViewModels;
using System.Windows.Threading;
using PortfolioSaver.Render.Services;

namespace PortfolioSaver.Render.Controls;

public partial class TickerTapeControl : UserControl
{
    private const double TapeCopySpacing = 20d;
    private readonly TapeAnimationController _animationController = new();
    private readonly Dictionary<TapeItemViewModel, List<Border>> _flashTargets = [];
    private readonly HashSet<TapeItemViewModel> _subscribedItems = [];
    private bool _metricsQueued;
    private TapeViewModel? _tape;
    private string _contentSignature = string.Empty;
    private int _renderedSideCopyCount;
    private bool _isLoaded;

    internal TapeAnimationController AnimationControllerForTests => _animationController;

    internal void RefreshMotionMetricsForTests() => RefreshMotionMetrics();

    public TickerTapeControl()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        if (_tape is null)
            SubscribeToTape(DataContext as TapeViewModel);

        _animationController.Attach(TrackPanel);
        // Start only after RefreshMotionMetrics has measured valid motion parameters.
        QueueMetricsUpdate();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = false;
        _animationController.Stop();
        UnsubscribeFromTape(_tape);
        ClearFlashTargets();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => QueueMetricsUpdate();

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OldValue, e.NewValue))
        {
            UnsubscribeFromTape(e.OldValue as TapeViewModel);
            SubscribeToTape(e.NewValue as TapeViewModel);
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
        if (!_isLoaded)
        {
            _animationController.Stop();
            return;
        }

        if (DataContext is not TapeViewModel tape || tape.Items.Count == 0)
        {
            TrackPanel.Children.Clear();
            TrackPanel.Width = 0d;
            _contentSignature = string.Empty;
            _renderedSideCopyCount = 0;
            ClearFlashTargets();
            _animationController.Stop();
            return;
        }

        UpdateLayout();
        double viewportWidth = ViewportHost.ActualWidth;
        if (viewportWidth <= 0)
        {
            TrackPanel.Children.Clear();
            TrackPanel.Width = 0d;
            _contentSignature = string.Empty;
            _renderedSideCopyCount = 0;
            ClearFlashTargets();
            _animationController.Stop();
            return;
        }

        RefreshItemSubscriptions();

        string signature = BuildContentSignature(tape);
        double sequenceWidth = MeasureContentWidth(tape);
        if (sequenceWidth <= 0)
        {
            TrackPanel.Children.Clear();
            TrackPanel.Width = 0d;
            _contentSignature = string.Empty;
            _renderedSideCopyCount = 0;
            ClearFlashTargets();
            _animationController.Stop();
            return;
        }

        int requiredSideCopies = CalculateSideCopyCount(viewportWidth, sequenceWidth);
        if (!string.Equals(signature, _contentSignature, StringComparison.Ordinal) || requiredSideCopies != _renderedSideCopyCount)
        {
            RebuildTrack(tape, requiredSideCopies, sequenceWidth);
            _contentSignature = signature;
            _renderedSideCopyCount = requiredSideCopies;
        }

        double cycleDistance = sequenceWidth + TapeCopySpacing;
        double pixelsPerSecond = Math.Max(18d, 72d * Math.Max(0.1d, tape.Speed));
        double anchorOffset = -requiredSideCopies * cycleDistance;
        _animationController.Update(cycleDistance, pixelsPerSecond, tape.Direction, anchorOffset);
        _animationController.Start();
    }

    private void SubscribeToTape(TapeViewModel? tape)
    {
        _tape = tape;
        _contentSignature = string.Empty;
        _renderedSideCopyCount = 0;
        if (_tape is null)
            return;

        _tape.PropertyChanged += OnTapePropertyChanged;
        _tape.Items.CollectionChanged += OnTapeItemsCollectionChanged;
        RefreshItemSubscriptions();
    }

    private void UnsubscribeFromTape(TapeViewModel? tape)
    {
        if (tape is null)
            return;

        tape.PropertyChanged -= OnTapePropertyChanged;
        tape.Items.CollectionChanged -= OnTapeItemsCollectionChanged;
        ClearItemSubscriptions();
        _contentSignature = string.Empty;
        _renderedSideCopyCount = 0;
        if (ReferenceEquals(_tape, tape))
            _tape = null;
    }

    private void OnTapePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TapeViewModel.Speed) or nameof(TapeViewModel.Direction))
            QueueMetricsUpdate();
    }

    private void OnTapeItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshItemSubscriptions();
        QueueMetricsUpdate();
    }

    private void RefreshItemSubscriptions()
    {
        if (_tape is null)
        {
            ClearItemSubscriptions();
            return;
        }

        HashSet<TapeItemViewModel> desired = _tape.Items.ToHashSet();
        foreach (TapeItemViewModel item in _subscribedItems.Except(desired).ToList())
        {
            item.PropertyChanged -= OnTapeItemPropertyChanged;
            _subscribedItems.Remove(item);
        }

        foreach (TapeItemViewModel item in desired)
        {
            if (_subscribedItems.Add(item))
                item.PropertyChanged += OnTapeItemPropertyChanged;
        }
    }

    private void ClearItemSubscriptions()
    {
        foreach (TapeItemViewModel item in _subscribedItems)
            item.PropertyChanged -= OnTapeItemPropertyChanged;

        _subscribedItems.Clear();
    }

    private void ClearFlashTargets()
    {
        _flashTargets.Clear();
    }

    private void OnTapeItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not TapeItemViewModel item || e.PropertyName != nameof(TapeItemViewModel.UpdateSequence))
            return;

        FlashValueTargets(item);
    }

    private double MeasureContentWidth(TapeViewModel tape)
    {
        FrameworkElement content = BuildSequencePanel(tape, registerTargets: false);
        content.Measure(new Size(double.PositiveInfinity, Math.Max(1d, ViewportHost.ActualHeight)));
        return Math.Max(content.ActualWidth, content.DesiredSize.Width);
    }

    private void RebuildTrack(TapeViewModel tape, int sideCopies, double sequenceWidth)
    {
        TrackPanel.Children.Clear();
        ClearFlashTargets();
        double sequenceSpan = sequenceWidth + TapeCopySpacing;
        int totalCopies = sideCopies * 2 + 1;

        for (int index = 0; index < totalCopies; index++)
        {
            FrameworkElement sequence = BuildSequencePanel(tape, registerTargets: true);
            Canvas.SetLeft(sequence, index * sequenceSpan);
            Canvas.SetTop(sequence, 0d);
            TrackPanel.Children.Add(sequence);
        }

        TrackPanel.Width = totalCopies * sequenceSpan;
        TrackPanel.Height = Math.Max(1d, ViewportHost.ActualHeight);
    }

    private static int CalculateSideCopyCount(double viewportWidth, double contentWidth)
    {
        double sequenceSpan = Math.Max(1d, contentWidth + TapeCopySpacing);
        return Math.Max(2, (int)Math.Ceiling(viewportWidth / sequenceSpan) + 2);
    }

    private static string BuildContentSignature(TapeViewModel tape) => $"{tape.Items.Count}";

    private FrameworkElement BuildSequencePanel(TapeViewModel tape, bool registerTargets)
    {
        StackPanel panel = new()
        {
            Orientation = Orientation.Horizontal,
            Height = 28
        };

        foreach (TapeItemViewModel item in tape.Items)
            panel.Children.Add(BuildItemPanel(item, registerTargets));

        return panel;
    }

    private FrameworkElement BuildItemPanel(TapeItemViewModel item, bool registerTargets)
    {
        StackPanel panel = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 18, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        panel.Children.Add(CreateSymbolHost(item));
        panel.Children.Add(CreateValueHost(item, nameof(TapeItemViewModel.LastText), nameof(TapeItemViewModel.LastForeground), 64d, 2d, TextAlignment.Right, FontWeights.SemiBold, registerTargets));
        panel.Children.Add(CreateValueHost(item, nameof(TapeItemViewModel.ChangeText), nameof(TapeItemViewModel.ChangeForeground), 72d, 0d, TextAlignment.Right, FontWeights.SemiBold, registerTargets));
        panel.Children.Add(new Border
        {
            Width = 1,
            Height = 16,
            Margin = new Thickness(9, 0, 0, 0),
            Background = new SolidColorBrush(Color.FromArgb(0x5A, 0x76, 0x8B, 0x9F)),
            VerticalAlignment = VerticalAlignment.Center
        });
        return panel;
    }

    private static FrameworkElement CreateSymbolHost(TapeItemViewModel item)
    {
        Grid host = new()
        {
            Width = 62d,
            Margin = new Thickness(0, 0, 2, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        TextBlock symbol = CreateBoundTextBlock(item, nameof(TapeItemViewModel.SymbolText), nameof(TapeItemViewModel.SymbolForeground), double.NaN, 0d, TextAlignment.Left, FontWeights.Bold);
        Grid.SetColumn(symbol, 0);
        host.Children.Add(symbol);

        FrameworkElement glyph = CreateWaitingGlyphHost(item);
        glyph.Margin = new Thickness(1, 0, 0, 0);
        Grid.SetColumn(glyph, 1);
        host.Children.Add(glyph);

        return host;
    }

    private Border CreateValueHost(
        TapeItemViewModel item,
        string textProperty,
        string foregroundProperty,
        double width,
        double rightMargin,
        TextAlignment alignment,
        FontWeight fontWeight,
        bool registerTarget,
        bool showWaitingGlyph = false)
    {
        Border border = new()
        {
            Width = width,
            Margin = new Thickness(0, 0, rightMargin, 0),
            Padding = new Thickness(2, 1, 2, 1),
            CornerRadius = new CornerRadius(4),
            Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (showWaitingGlyph)
        {
            Grid host = new();
            host.Children.Add(CreateBoundTextBlock(item, textProperty, foregroundProperty, double.NaN, 0d, alignment, fontWeight));
            host.Children.Add(CreateWaitingGlyphHost(item));
            border.Child = host;
        }
        else
        {
            border.Child = CreateBoundTextBlock(item, textProperty, foregroundProperty, double.NaN, 0d, alignment, fontWeight);
        }

        if (registerTarget) RegisterFlashTarget(item, border);
        return border;
    }

    private static FrameworkElement CreateWaitingGlyphHost(TapeItemViewModel item)
    {
        TextBlock glyph = new()
        {
            FontFamily = new FontFamily("Segoe UI Emoji"),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            TextAlignment = TextAlignment.Left,
            Opacity = 0.82
        };
        glyph.SetBinding(TextBlock.TextProperty, new Binding(nameof(TapeItemViewModel.WaitingGlyphText))
        {
            Source = item
        });
        glyph.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(TapeItemViewModel.WaitingGlyphForeground))
        {
            Source = item
        });
        glyph.SetBinding(TextBlock.VisibilityProperty, new Binding(nameof(TapeItemViewModel.IsWaitingOnData))
        {
            Source = item,
            Converter = new BooleanToVisibilityConverter()
        });
        return glyph;
    }

    private void RegisterFlashTarget(TapeItemViewModel item, Border border)
    {
        if (!_flashTargets.TryGetValue(item, out List<Border>? targets))
        {
            targets = [];
            _flashTargets[item] = targets;
        }

        targets.Add(border);
    }

    private void FlashValueTargets(TapeItemViewModel item)
    {
        if (!_flashTargets.TryGetValue(item, out List<Border>? targets) || targets.Count == 0)
            return;

        Color flashColor = ToColor(item.ValueFlashBrush);
        foreach (Border target in targets.Where(target => target is not null))
        {
            SolidColorBrush brush = target.Background as SolidColorBrush ?? new SolidColorBrush(Colors.Transparent);
            if (brush.IsFrozen)
                brush = brush.CloneCurrentValue();

            if (!ReferenceEquals(target.Background, brush))
                target.Background = brush;

            brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            brush.Color = Colors.Transparent;

            ColorAnimationUsingKeyFrames animation = new();
            animation.KeyFrames.Add(new DiscreteColorKeyFrame(Colors.Transparent, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            animation.KeyFrames.Add(new LinearColorKeyFrame(flashColor, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(150))));
            animation.KeyFrames.Add(new LinearColorKeyFrame(flashColor, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(740))));
            animation.KeyFrames.Add(new LinearColorKeyFrame(Colors.Transparent, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1680))));
            brush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
        }
    }

    private static Color ToColor(Brush brush)
    {
        if (brush is SolidColorBrush solid)
            return Color.FromArgb(240, solid.Color.R, solid.Color.G, solid.Color.B);

        return Color.FromArgb(240, 255, 196, 64);
    }

    private static TextBlock CreateBoundTextBlock(
        TapeItemViewModel item,
        string textProperty,
        string foregroundProperty,
        double width,
        double rightMargin,
        TextAlignment alignment,
        FontWeight fontWeight)
    {
        TextBlock block = new()
        {
            Width = double.IsNaN(width) ? double.NaN : width,
            Margin = new Thickness(0, 0, rightMargin, 0),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 15,
            FontWeight = fontWeight,
            Foreground = Brushes.WhiteSmoke,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = alignment,
            VerticalAlignment = VerticalAlignment.Center
        };

        BindingOperations.SetBinding(block, TextBlock.TextProperty, new Binding(textProperty) { Source = item });
        BindingOperations.SetBinding(block, TextBlock.ForegroundProperty, new Binding(foregroundProperty) { Source = item });
        return block;
    }
}
