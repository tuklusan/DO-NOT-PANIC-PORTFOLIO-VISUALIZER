using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace PortfolioSaver.Media.Services;

public sealed class ImageTransitionController
{
    public void FadeTo(Image image, double durationSeconds = 1.2)
    {
        DoubleAnimation animation = new(0, 1, TimeSpan.FromSeconds(durationSeconds));
        image.BeginAnimation(UIElement.OpacityProperty, animation);
    }
}
