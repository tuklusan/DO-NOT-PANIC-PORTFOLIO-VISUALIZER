using System.Collections.ObjectModel;
using PortfolioSaver.Core.Constants;
using PortfolioSaver.Shared.Infrastructure;

namespace PortfolioSaver.Render.ViewModels;

public sealed class NewsFlasherViewModel : BindableBase
{
    private string _title = "FINANCE NEWS";
    private double _speed = Defaults.DefaultNewsSpeed;

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public double Speed
    {
        get => _speed;
        set => SetProperty(ref _speed, value);
    }

    public ObservableCollection<NewsHeadlineViewModel> Headlines { get; set; } = [];
}
