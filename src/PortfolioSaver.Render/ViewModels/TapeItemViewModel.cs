using System.Windows.Media;
using PortfolioSaver.Shared.Infrastructure;

namespace PortfolioSaver.Render.ViewModels;

public sealed class TapeItemViewModel : BindableBase
{
    private string _symbolText = string.Empty;
    private string _lastText = string.Empty;
    private string _changeText = string.Empty;
    private Brush _symbolForeground = Brushes.DarkOrange;
    private Brush _lastForeground = Brushes.White;
    private Brush _changeForeground = Brushes.White;
    private Brush _valueFlashBrush = Brushes.Transparent;
    private bool _isWaitingOnData;
    private int _updateSequence;
    private long _quoteUpdateToken;

    public string SymbolText
    {
        get => _symbolText;
        set => SetProperty(ref _symbolText, value);
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

    public Brush SymbolForeground
    {
        get => _symbolForeground;
        set => SetProperty(ref _symbolForeground, value);
    }

    public Brush LastForeground
    {
        get => _lastForeground;
        set => SetProperty(ref _lastForeground, value);
    }

    public Brush ChangeForeground
    {
        get => _changeForeground;
        set => SetProperty(ref _changeForeground, value);
    }

    public Brush ValueFlashBrush
    {
        get => _valueFlashBrush;
        set => SetProperty(ref _valueFlashBrush, value);
    }

    public bool IsWaitingOnData
    {
        get => _isWaitingOnData;
        set => SetProperty(ref _isWaitingOnData, value);
    }

    public int UpdateSequence
    {
        get => _updateSequence;
        set => SetProperty(ref _updateSequence, value);
    }

    public long QuoteUpdateToken
    {
        get => _quoteUpdateToken;
        set => SetProperty(ref _quoteUpdateToken, value);
    }

    public void TriggerValueFlash(Brush? brush)
    {
        ValueFlashBrush = brush ?? Brushes.Goldenrod;
        UpdateSequence++;
    }
}
