using System.Windows.Media;
using PortfolioSaver.Shared.Infrastructure;

namespace PortfolioSaver.Render.ViewModels;

public sealed class NewsHeadlineViewModel : BindableBase
{
    private string _text = string.Empty;
    private Brush _foreground = Brushes.WhiteSmoke;

    public string Text
    {
        get => _text;
        set => SetProperty(ref _text, value);
    }

    public Brush Foreground
    {
        get => _foreground;
        set => SetProperty(ref _foreground, value);
    }
}
