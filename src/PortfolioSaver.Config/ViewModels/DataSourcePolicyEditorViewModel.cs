using PortfolioSaver.Core.Constants;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Core.Enums;
using PortfolioSaver.Shared.Infrastructure;

namespace PortfolioSaver.Config.ViewModels;

public sealed class DataSourcePolicyEditorViewModel : BindableBase
{
    private readonly DataSourceCapabilities _capabilities;
    private int _maxQueriesPerHour;
    private int _maxQueriesPerDay;
    private bool _enableSingleTickerQueries;
    private bool _enableBatchTickerQueries;

    public DataSourcePolicyEditorViewModel(DataSourcePolicySettings settings)
    {
        _capabilities = DataSourceCatalog.GetCapabilities(settings.Kind);
        _maxQueriesPerHour = Math.Clamp(settings.MaxQueriesPerHour, 1, _capabilities.HardMaxQueriesPerHour);
        _maxQueriesPerDay = Math.Clamp(settings.MaxQueriesPerDay, 1, _capabilities.HardMaxQueriesPerDay);
        _enableSingleTickerQueries = _capabilities.SupportsSingleTickerQueries && settings.EnableSingleTickerQueries;
        _enableBatchTickerQueries = _capabilities.SupportsBatchTickerQueries && settings.EnableBatchTickerQueries;
    }

    public DataSourceKind Kind => _capabilities.Kind;
    public string DisplayName => _capabilities.DisplayName;
    public int HardMaxQueriesPerHour => _capabilities.HardMaxQueriesPerHour;
    public int HardMaxQueriesPerDay => _capabilities.HardMaxQueriesPerDay;
    public string KnownLimitText => $"Max {HardMaxQueriesPerHour}/hour, {HardMaxQueriesPerDay}/day";
    public bool CanUseSingleTickerQueries => _capabilities.SupportsSingleTickerQueries;
    public bool CanUseBatchTickerQueries => _capabilities.SupportsBatchTickerQueries;

    public int MaxQueriesPerHour
    {
        get => _maxQueriesPerHour;
        set => SetProperty(ref _maxQueriesPerHour, Math.Clamp(value, 1, HardMaxQueriesPerHour));
    }

    public int MaxQueriesPerDay
    {
        get => _maxQueriesPerDay;
        set => SetProperty(ref _maxQueriesPerDay, Math.Clamp(value, 1, HardMaxQueriesPerDay));
    }

    public bool EnableSingleTickerQueries
    {
        get => _enableSingleTickerQueries;
        set => SetProperty(ref _enableSingleTickerQueries, CanUseSingleTickerQueries && value);
    }

    public bool EnableBatchTickerQueries
    {
        get => _enableBatchTickerQueries;
        set => SetProperty(ref _enableBatchTickerQueries, CanUseBatchTickerQueries && value);
    }

    public DataSourcePolicySettings ToModel() => new()
    {
        Kind = Kind,
        MaxQueriesPerHour = MaxQueriesPerHour,
        MaxQueriesPerDay = MaxQueriesPerDay,
        EnableSingleTickerQueries = EnableSingleTickerQueries,
        EnableBatchTickerQueries = EnableBatchTickerQueries
    };

    public void ApplyModel(DataSourcePolicySettings settings)
    {
        MaxQueriesPerHour = settings.MaxQueriesPerHour;
        MaxQueriesPerDay = settings.MaxQueriesPerDay;
        EnableSingleTickerQueries = settings.EnableSingleTickerQueries;
        EnableBatchTickerQueries = settings.EnableBatchTickerQueries;
    }
}
