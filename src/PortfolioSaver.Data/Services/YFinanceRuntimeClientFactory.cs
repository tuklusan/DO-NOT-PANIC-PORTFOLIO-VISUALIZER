using PortfolioSaver.Shared.Helpers;
using YFinance.NET.Api;
using YFinance.NET.Config;

namespace PortfolioSaver.Data.Services;

public static class YFinanceRuntimeClientFactory
{
    private static readonly object Sync = new();
    private static YFinanceClient? _sharedClient;

    public static YFinanceClient GetSharedClient()
    {
        lock (Sync)
        {
            _sharedClient ??= new YFinanceClient(new YFinanceOptions
            {
                MinimumRequestSpacing = TimeSpan.FromMilliseconds(500),
                MaxRetries = 3,
                DefaultCacheTtl = TimeSpan.FromMinutes(10),
                SummaryCacheTtl = TimeSpan.FromHours(6),
                PersistentMetadataCacheTtl = TimeSpan.FromHours(12),
                MaxSymbolsPerQuoteRequest = 25,
                TraceSink = PortfolioSaverYFinanceTraceSink.Instance,
                PersistentCacheRootPath = Path.Combine(PathHelper.GetLocalDataDirectory(), "YFinance.NET", "cache")
            });

            return _sharedClient;
        }
    }
}
