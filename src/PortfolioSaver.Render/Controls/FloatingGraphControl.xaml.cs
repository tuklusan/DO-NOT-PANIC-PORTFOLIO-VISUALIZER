using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using PortfolioSaver.Render.ViewModels;

namespace PortfolioSaver.Render.Controls;

public partial class FloatingGraphControl : UserControl
{
    private static readonly Color BaseCardColor = Color.FromArgb(0x7A, 0x0D, 0x13, 0x1B);
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
        if (_graph is null)
            return;

        if (e.PropertyName == nameof(FloatingGraphViewModel.FlashSequence))
        {
            if (!_graph.IsRefreshTravelFlashActive)
                BeginCardFlash(_graph.FlashBrush);

            return;
        }

        if (e.PropertyName == nameof(FloatingGraphViewModel.IsRefreshTravelFlashActive))
        {
            if (_graph.IsRefreshTravelFlashActive)
                BeginSustainedCardFlash(_graph.FlashBrush);
            else
                EndSustainedCardFlash();
        }
    }

    private void BeginCardFlash(Brush flashBrush)
    {
        Color flashColor = flashBrush is SolidColorBrush solid
            ? Color.FromArgb(236, solid.Color.R, solid.Color.G, solid.Color.B)
            : Color.FromArgb(236, 255, 196, 64);

        SolidColorBrush baseBrush = RootBorder.Background as SolidColorBrush ?? new SolidColorBrush(BaseCardColor);
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

    private void BeginSustainedCardFlash(Brush flashBrush)
    {
        Color flashColor = flashBrush is SolidColorBrush solid
            ? Color.FromArgb(236, solid.Color.R, solid.Color.G, solid.Color.B)
            : Color.FromArgb(236, 255, 196, 64);

        SolidColorBrush baseBrush = RootBorder.Background as SolidColorBrush ?? new SolidColorBrush(BaseCardColor);
        RootBorder.Background = baseBrush;
        baseBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);

        ColorAnimation animation = new()
        {
            From = BaseCardColor,
            To = flashColor,
            Duration = TimeSpan.FromMilliseconds(220),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };

        baseBrush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
    }

    private void EndSustainedCardFlash()
    {
        SolidColorBrush baseBrush = RootBorder.Background as SolidColorBrush ?? new SolidColorBrush(BaseCardColor);
        RootBorder.Background = baseBrush;
        baseBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);
        baseBrush.Color = BaseCardColor;
    }
}


