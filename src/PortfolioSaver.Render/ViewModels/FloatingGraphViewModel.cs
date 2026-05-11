using System.Windows;
using System.Windows.Media;

namespace PortfolioSaver.Render.ViewModels;

public sealed class FloatingGraphViewModel : FloatingSpriteViewModel
{
    private string _symbol = string.Empty;
    private string _tapeName = string.Empty;
    private string? _overlayText;
    private string _lastText = "--";
    private string _changeText = "--";
    private Brush _changeForeground = Brushes.Gainsboro;
    private string _maxScaleText = string.Empty;
    private string _midScaleText = string.Empty;
    private string _minScaleText = string.Empty;
    private string _leftTimeScaleText = string.Empty;
    private string _middleTimeScaleText = string.Empty;
    private string _rightTimeScaleText = string.Empty;
    private double _plotWidth;
    private double _plotHeight;
    private Brush _cardBackground = new SolidColorBrush(Color.FromArgb(0x7A, 0x0D, 0x13, 0x1B));
    private Brush _cardBorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0x8A, 0xA2, 0xB8));
    private Brush _flashBrush = Brushes.Transparent;
    private bool _isVisible = true;
    private int _flashSequence;
    private long _quoteUpdateToken;
    private double _nominalVelocityX;
    private double _nominalVelocityY;
    private double? _refreshTravelTargetY;
    private bool _isRefreshTravelFlashActive;
    private PointCollection _greenPoints = [];
    private PointCollection _redPoints = [];

    public string Symbol
    {
        get => _symbol;
        set => SetProperty(ref _symbol, value);
    }

    public string TapeName
    {
        get => _tapeName;
        set => SetProperty(ref _tapeName, value);
    }

    public string? OverlayText
    {
        get => _overlayText;
        set => SetProperty(ref _overlayText, value);
    }

    public string LastText
    {
        get => _lastText;
        set => SetProperty(ref _lastText, value);
    }

    public string ChangeText
    {
        get => _changeText;
        set => SetProperty(ref _changeText, value);
    }

    public Brush ChangeForeground
    {
        get => _changeForeground;
        set => SetProperty(ref _changeForeground, value);
    }

    public string MaxScaleText
    {
        get => _maxScaleText;
        set => SetProperty(ref _maxScaleText, value);
    }

    public string MidScaleText
    {
        get => _midScaleText;
        set => SetProperty(ref _midScaleText, value);
    }

    public string MinScaleText
    {
        get => _minScaleText;
        set => SetProperty(ref _minScaleText, value);
    }

    public string LeftTimeScaleText
    {
        get => _leftTimeScaleText;
        set => SetProperty(ref _leftTimeScaleText, value);
    }

    public string MiddleTimeScaleText
    {
        get => _middleTimeScaleText;
        set => SetProperty(ref _middleTimeScaleText, value);
    }

    public string RightTimeScaleText
    {
        get => _rightTimeScaleText;
        set => SetProperty(ref _rightTimeScaleText, value);
    }

    public double PlotWidth
    {
        get => _plotWidth;
        set => SetProperty(ref _plotWidth, value);
    }

    public double PlotHeight
    {
        get => _plotHeight;
        set => SetProperty(ref _plotHeight, value);
    }

    public Brush CardBackground
    {
        get => _cardBackground;
        set => SetProperty(ref _cardBackground, value);
    }

    public Brush CardBorderBrush
    {
        get => _cardBorderBrush;
        set => SetProperty(ref _cardBorderBrush, value);
    }

    public Brush FlashBrush
    {
        get => _flashBrush;
        set => SetProperty(ref _flashBrush, value);
    }

    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }

    public int FlashSequence
    {
        get => _flashSequence;
        set => SetProperty(ref _flashSequence, value);
    }

    public long QuoteUpdateToken
    {
        get => _quoteUpdateToken;
        set => SetProperty(ref _quoteUpdateToken, value);
    }

    public double NominalVelocityX
    {
        get => _nominalVelocityX;
        set => SetProperty(ref _nominalVelocityX, value);
    }

    public double NominalVelocityY
    {
        get => _nominalVelocityY;
        set => SetProperty(ref _nominalVelocityY, value);
    }

    public double? RefreshTravelTargetY
    {
        get => _refreshTravelTargetY;
        set => SetProperty(ref _refreshTravelTargetY, value);
    }

    public bool IsRefreshTravelFlashActive
    {
        get => _isRefreshTravelFlashActive;
        set => SetProperty(ref _isRefreshTravelFlashActive, value);
    }

    public List<GraphPointViewModel> Points { get; set; } = [];

    public PointCollection GreenPoints
    {
        get => _greenPoints;
        set => SetProperty(ref _greenPoints, value);
    }

    public PointCollection RedPoints
    {
        get => _redPoints;
        set => SetProperty(ref _redPoints, value);
    }

    public List<PointCollection> GreenSegments { get; set; } = [];
    public List<PointCollection> RedSegments { get; set; } = [];

    public void TriggerCardFlash(Brush? brush)
    {
        FlashBrush = brush ?? Brushes.Goldenrod;
        FlashSequence++;
    }
}
