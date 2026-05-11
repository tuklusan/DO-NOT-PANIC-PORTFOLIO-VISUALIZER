using System.Windows.Media;

namespace PortfolioSaver.Render.ViewModels;

public sealed class MarketSpriteViewModel : FloatingSpriteViewModel
{
    private string _spriteText = string.Empty;
    private Brush _foreground = Brushes.White;
    private double _scaleX = 1d;
    private double _baseY;
    private double _phase;
    private bool _isBag;
    private string _key = string.Empty;

    public string Key
    {
        get => _key;
        set => SetProperty(ref _key, value);
    }

    public string SpriteText
    {
        get => _spriteText;
        set => SetProperty(ref _spriteText, value);
    }

    public Brush Foreground
    {
        get => _foreground;
        set => SetProperty(ref _foreground, value);
    }

    public double ScaleX
    {
        get => _scaleX;
        set => SetProperty(ref _scaleX, value);
    }

    public double BaseY
    {
        get => _baseY;
        set => SetProperty(ref _baseY, value);
    }

    public double Phase
    {
        get => _phase;
        set => SetProperty(ref _phase, value);
    }

    public bool IsBag
    {
        get => _isBag;
        set => SetProperty(ref _isBag, value);
    }
}
