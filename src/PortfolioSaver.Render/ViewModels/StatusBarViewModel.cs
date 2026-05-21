using PortfolioSaver.Shared.Infrastructure;
using System.Collections.ObjectModel;
using System.Windows.Media;

namespace PortfolioSaver.Render.ViewModels;

public sealed class StatusBarViewModel : BindableBase
{
    private string _marketStatusText = "Market: --";
    private string _providerText = "Provider: --";
    private string _updatedText = "Updated: --";
    private string _updatedPrefixText = "Last Updated:";
    private string _updatedSymbolText = string.Empty;
    private string _updatedAgeText = "--";
    private Brush _updatedSymbolForeground = Brushes.Gainsboro;
    private string _clockDateText = DateTime.Now.ToString("ddd dd-MMM-yyyy").ToUpperInvariant();
    private string _clockText = DateTime.Now.ToLongTimeString();
    private ObservableCollection<MacroMeterViewModel> _macroMeters = [];

    public string MarketStatusText
    {
        get => _marketStatusText;
        set => SetProperty(ref _marketStatusText, value);
    }

    public string ProviderText
    {
        get => _providerText;
        set => SetProperty(ref _providerText, value);
    }

    public string UpdatedText
    {
        get => _updatedText;
        set => SetProperty(ref _updatedText, value);
    }

    public string UpdatedPrefixText
    {
        get => _updatedPrefixText;
        set => SetProperty(ref _updatedPrefixText, value);
    }

    public string UpdatedSymbolText
    {
        get => _updatedSymbolText;
        set => SetProperty(ref _updatedSymbolText, value);
    }

    public string UpdatedAgeText
    {
        get => _updatedAgeText;
        set => SetProperty(ref _updatedAgeText, value);
    }

    public Brush UpdatedSymbolForeground
    {
        get => _updatedSymbolForeground;
        set => SetProperty(ref _updatedSymbolForeground, value);
    }

    public string ClockText
    {
        get => _clockText;
        set => SetProperty(ref _clockText, value);
    }

    public string ClockDateText
    {
        get => _clockDateText;
        set => SetProperty(ref _clockDateText, value);
    }

    public ObservableCollection<MacroMeterViewModel> MacroMeters
    {
        get => _macroMeters;
        set => SetProperty(ref _macroMeters, value);
    }
}
