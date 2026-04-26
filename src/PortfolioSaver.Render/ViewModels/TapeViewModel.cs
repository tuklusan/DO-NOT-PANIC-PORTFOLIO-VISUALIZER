using PortfolioSaver.Core.Enums;
using System.Collections.ObjectModel;
using PortfolioSaver.Shared.Infrastructure;

namespace PortfolioSaver.Render.ViewModels;

public sealed class TapeViewModel : BindableBase
{
    private string _title = string.Empty;
    private double _speed = 1.0d;
    private ScrollDirection _direction = ScrollDirection.Left;

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

    public ScrollDirection Direction
    {
        get => _direction;
        set => SetProperty(ref _direction, value);
    }

    public ObservableCollection<TapeItemViewModel> Items { get; set; } = [];
}
