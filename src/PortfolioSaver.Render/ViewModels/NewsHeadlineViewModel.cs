using System.Windows.Media;
using PortfolioSaver.Shared.Infrastructure;

namespace PortfolioSaver.Render.ViewModels;

public sealed class NewsHeadlineViewModel : BindableBase
{
    private string _text = string.Empty;
    private Brush _foreground = Brushes.WhiteSmoke;
    private bool _isSupplemental;

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

    public bool IsSupplemental
    {
        get => _isSupplemental;
        set => SetProperty(ref _isSupplemental, value);
    }
}
