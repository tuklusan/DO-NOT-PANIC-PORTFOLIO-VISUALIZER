using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using PortfolioSaver.Render.ViewModels;

namespace PortfolioSaver.Render.Controls;

public partial class FloatingGraphControl : UserControl
{
    private FloatingGraphViewModel? _graph;

    public FloatingGraphControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OldValue, e.NewValue))
        {
            if (e.OldValue is FloatingGraphViewModel oldGraph)
                oldGraph.PropertyChanged -= OnGraphPropertyChanged;

            _graph = e.NewValue as FloatingGraphViewModel;
            if (_graph is not null)
                _graph.PropertyChanged += OnGraphPropertyChanged;
        }
    }

    private void OnGraphPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_graph is null || e.PropertyName != nameof(FloatingGraphViewModel.FlashSequence))
            return;

        BeginCardFlash(_graph.FlashBrush);
    }

    private void BeginCardFlash(Brush flashBrush)
    {
        Color flashColor = flashBrush is SolidColorBrush solid
            ? Color.FromArgb(236, solid.Color.R, solid.Color.G, solid.Color.B)
            : Color.FromArgb(236, 255, 196, 64);

        SolidColorBrush baseBrush = RootBorder.Background as SolidColorBrush ?? new SolidColorBrush(Color.FromArgb(0x7A, 0x0D, 0x13, 0x1B));
        RootBorder.Background = baseBrush;
        baseBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);
        Color baseColor = baseBrush.Color;

        ColorAnimationUsingKeyFrames animation = new();
        animation.KeyFrames.Add(new DiscreteColorKeyFrame(baseColor, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new LinearColorKeyFrame(flashColor, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(180))));
        animation.KeyFrames.Add(new LinearColorKeyFrame(baseColor, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(620))));
        animation.KeyFrames.Add(new LinearColorKeyFrame(flashColor, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(980))));
        animation.KeyFrames.Add(new LinearColorKeyFrame(baseColor, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1680))));
        baseBrush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
    }
}


