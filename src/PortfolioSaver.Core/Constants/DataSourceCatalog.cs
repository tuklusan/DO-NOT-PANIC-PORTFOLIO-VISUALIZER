using PortfolioSaver.Core.Enums;
using PortfolioSaver.Core.Models;

namespace PortfolioSaver.Core.Constants;

public static class DataSourceCatalog
{
    public static IReadOnlyList<DataSourceKind> OrderedKinds { get; } =
    [
        DataSourceKind.YahooFinance,
        DataSourceKind.Finnhub,
        DataSourceKind.TwelveData,
        DataSourceKind.Tiingo
    ];

    public static DataSourceCapabilities GetCapabilities(DataSourceKind kind) => kind switch
    {
        DataSourceKind.Finnhub => new(
            kind,
            "Finnhub",
            HardMaxQueriesPerMinute: 0,
            HardMaxQueriesPerHour: 3600,
            HardMaxQueriesPerDay: 86400,
            SupportsSingleTickerQueries: true,
            SupportsBatchTickerQueries: false),
        DataSourceKind.TwelveData => new(
            kind,
            "Twelve Data",
            HardMaxQueriesPerMinute: 8,
            HardMaxQueriesPerHour: 480,
            HardMaxQueriesPerDay: 800,
            SupportsSingleTickerQueries: true,
            SupportsBatchTickerQueries: true),
        DataSourceKind.Tiingo => new(
            kind,
            "Tiingo",
            HardMaxQueriesPerMinute: 0,
            HardMaxQueriesPerHour: 50,
            HardMaxQueriesPerDay: 1000,
            SupportsSingleTickerQueries: true,
            SupportsBatchTickerQueries: false),
        DataSourceKind.YahooFinance => new(
            kind,
            "Yahoo Finance v8",
            HardMaxQueriesPerMinute: 0,
            HardMaxQueriesPerHour: 2000,
            HardMaxQueriesPerDay: 48000,
            SupportsSingleTickerQueries: true,
            SupportsBatchTickerQueries: true),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public static IReadOnlyList<DataSourcePolicySettings> CreateDefaultPolicies()
        => OrderedKinds.Select(CreateDefaultPolicy).ToList();

    public static DataSourcePolicySettings CreateDefaultPolicy(DataSourceKind kind)
    {
        DataSourceCapabilities capabilities = GetCapabilities(kind);
        return new DataSourcePolicySettings
        {
            Kind = kind,
            MaxQueriesPerHour = capabilities.HardMaxQueriesPerHour,
            MaxQueriesPerDay = capabilities.HardMaxQueriesPerDay,
            EnableSingleTickerQueries = capabilities.SupportsSingleTickerQueries,
            EnableBatchTickerQueries = capabilities.SupportsBatchTickerQueries
        };
    }

    public static IReadOnlyList<DataSourcePolicySettings> NormalizePolicies(IEnumerable<DataSourcePolicySettings>? sourcePolicies)
    {
        Dictionary<DataSourceKind, DataSourcePolicySettings> byKind = (sourcePolicies ?? [])
            .GroupBy(policy => policy.Kind)
            .Select(group => group.Last())
            .ToDictionary(policy => policy.Kind);

        List<DataSourcePolicySettings> normalized = [];
        foreach (DataSourceKind kind in OrderedKinds)
        {
            DataSourceCapabilities capabilities = GetCapabilities(kind);
            DataSourcePolicySettings source = byKind.TryGetValue(kind, out DataSourcePolicySettings? existing)
                ? existing
                : CreateDefaultPolicy(kind);

            normalized.Add(new DataSourcePolicySettings
            {
                Kind = kind,
                MaxQueriesPerHour = Math.Clamp(source.MaxQueriesPerHour <= 0 ? capabilities.HardMaxQueriesPerHour : source.MaxQueriesPerHour, 1, capabilities.HardMaxQueriesPerHour),
                MaxQueriesPerDay = Math.Clamp(source.MaxQueriesPerDay <= 0 ? capabilities.HardMaxQueriesPerDay : source.MaxQueriesPerDay, 1, capabilities.HardMaxQueriesPerDay),
                EnableSingleTickerQueries = capabilities.SupportsSingleTickerQueries && source.EnableSingleTickerQueries,
                EnableBatchTickerQueries = capabilities.SupportsBatchTickerQueries && source.EnableBatchTickerQueries
            });
        }

        return normalized;
    }
}

public sealed record DataSourceCapabilities(
    DataSourceKind Kind,
    string DisplayName,
    int HardMaxQueriesPerMinute,
    int HardMaxQueriesPerHour,
    int HardMaxQueriesPerDay,
    bool SupportsSingleTickerQueries,
    bool SupportsBatchTickerQueries);
