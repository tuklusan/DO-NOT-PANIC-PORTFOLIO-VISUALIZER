using System.Collections.ObjectModel;

namespace PortfolioSaver.Render.ViewModels;

public sealed class FloatingClockViewModel : FloatingSpriteViewModel
{
    private string _title = "Clocks";
    private string _subtitle = string.Empty;

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string Subtitle
    {
        get => _subtitle;
        set => SetProperty(ref _subtitle, value);
    }

    public ObservableCollection<ClockCityViewModel> Cities { get; } = [];
}
