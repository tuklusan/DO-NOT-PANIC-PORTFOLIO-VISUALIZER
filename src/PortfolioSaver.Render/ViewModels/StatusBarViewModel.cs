using PortfolioSaver.Shared.Infrastructure;
using System.Collections.ObjectModel;

namespace PortfolioSaver.Render.ViewModels;

public sealed class StatusBarViewModel : BindableBase
{
    private string _marketStatusText = "Market: --";
    private string _providerText = "Provider: --";
    private string _updatedText = "Updated: --";
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
