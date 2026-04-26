using System.Collections.ObjectModel;
using System.Windows;
using PortfolioSaver.Config.Commands;
using PortfolioSaver.Core.Constants;
using PortfolioSaver.Core.Enums;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Shared.Infrastructure;

namespace PortfolioSaver.Config.ViewModels;

public sealed class TickerGroupEditorViewModel : BindableBase
{
    private string _name;
    private bool _enabled;
    private double _speedValue;
    private RenderMode _renderMode;
    private ScrollDirection _direction;
    private double _rowHeight;
    private readonly Action<TickerGroupEditorViewModel>? _removeAction;

    public TickerGroupEditorViewModel(TickerGroup? group = null, Action<TickerGroupEditorViewModel>? removeAction = null)
    {
        group ??= new TickerGroup();
        _removeAction = removeAction;
        _name = group.Name;
        _enabled = group.Enabled;
        _speedValue = Math.Clamp(
            group.Speed <= 0 ? Defaults.DefaultTapeBaseSpeed : group.Speed,
            Defaults.MinTapeSpeed,
            Defaults.MaxTapeSpeed);
        _renderMode = group.RenderMode;
        _direction = group.Direction;
        _rowHeight = group.RowHeight;
        Tickers = new ObservableCollection<TickerItemEditorViewModel>(
            group.Tickers
                .Take(Defaults.MaxTickersPerTape)
                .Select(item => new TickerItemEditorViewModel(item, RemoveTicker)));
        AddTickerCommand = new RelayCommand(AddTicker);
        RemoveGroupCommand = new RelayCommand(() => _removeAction?.Invoke(this));
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public bool Enabled
    {
        get => _enabled;
        set => SetProperty(ref _enabled, value);
    }

    public double SpeedValue
    {
        get => _speedValue;
        set
        {
            double normalized = Math.Round(Math.Clamp(value, Defaults.MinTapeSpeed, Defaults.MaxTapeSpeed), 3);
            if (SetProperty(ref _speedValue, normalized))
                RaisePropertyChanged(nameof(SpeedPresetLabel));
        }
    }

    public string SpeedPresetLabel => _speedValue switch
    {
        < 0.42d => "Slow",
        > 0.48d => "Fast",
        _ => "Normal"
    };

    public string TickerSlotsLabel => $"{Tickers.Count}/{Defaults.MaxTickersPerTape} tickers";

    public RenderMode RenderMode
    {
        get => _renderMode;
        set => SetProperty(ref _renderMode, value);
    }

    public ScrollDirection Direction
    {
        get => _direction;
        set => SetProperty(ref _direction, value);
    }

    public double RowHeight
    {
        get => _rowHeight;
        set => SetProperty(ref _rowHeight, value);
    }

    public ObservableCollection<TickerItemEditorViewModel> Tickers { get; }

    public RelayCommand AddTickerCommand { get; }

    public RelayCommand RemoveGroupCommand { get; }

    private void AddTicker()
    {
        if (Tickers.Count >= Defaults.MaxTickersPerTape)
        {
            MessageBox.Show(
                $"Each tape can contain up to {Defaults.MaxTickersPerTape} tickers.",
                "Ticker Limit Reached",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        Tickers.Add(new TickerItemEditorViewModel(removeAction: RemoveTicker));
        RaisePropertyChanged(nameof(TickerSlotsLabel));
    }

    private void RemoveTicker(TickerItemEditorViewModel item)
    {
        Tickers.Remove(item);
        RaisePropertyChanged(nameof(TickerSlotsLabel));
    }

    public TickerGroup ToModel()
    {
        return new TickerGroup
        {
            Name = string.IsNullOrWhiteSpace(Name) ? "New Group" : Name.Trim(),
            Enabled = Enabled,
            Speed = SpeedValue,
            RenderMode = RenderMode,
            Direction = Direction,
            RowHeight = RowHeight,
            Tickers = Tickers
                .Where(item => !string.IsNullOrWhiteSpace(item.Symbol))
                .Take(Defaults.MaxTickersPerTape)
                .Select(item => item.ToModel())
                .ToList()
        };
    }
}
