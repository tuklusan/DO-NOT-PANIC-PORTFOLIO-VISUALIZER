using PortfolioSaver.Core.Enums;

namespace PortfolioSaver.Core.Models;

public sealed class DataSourcePolicySettings
{
    public DataSourceKind Kind { get; set; }
    public int MaxQueriesPerHour { get; set; }
    public int MaxQueriesPerDay { get; set; }
    public bool EnableSingleTickerQueries { get; set; } = true;
    public bool EnableBatchTickerQueries { get; set; }
}
