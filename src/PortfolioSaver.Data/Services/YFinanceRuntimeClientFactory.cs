using PortfolioSaver.Shared.Helpers;
using PortfolioSaver.Shared.Diagnostics;
using YFinance.NET.Api;
using YFinance.NET.Config;

namespace PortfolioSaver.Data.Services;

public static class YFinanceRuntimeClientFactory
{
    private static readonly object Sync = new();
    private static readonly SemaphoreSlim ClientGate = new(1, 1);
    private static long _operationSequence;
    private static YFinanceClient? _sharedClient;

    public static YFinanceClient GetSharedClient()
    {
        lock (Sync)
        {
            _sharedClient ??= new YFinanceClient(new YFinanceOptions
            {
                MinimumRequestSpacing = TimeSpan.FromSeconds(1),
                MaxRetries = 3,
                DefaultCacheTtl = TimeSpan.FromMinutes(10),
                SummaryCacheTtl = TimeSpan.FromMinutes(10),
                PersistentMetadataCacheTtl = TimeSpan.FromMinutes(10),
                MaxSymbolsPerQuoteRequest = 25,
                TraceSink = PortfolioSaverYFinanceTraceSink.Instance,
                PersistentCacheRootPath = Path.Combine(PathHelper.GetLocalDataDirectory(), "YFinance.NET", "cache")
            });

            return _sharedClient;
        }
    }

    public static async Task<T> RunSerializedAsync<T>(string lane, Func<YFinanceClient, CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
        => await RunSerializedAsync(lane, CreateOperationId(lane), action, cancellationToken).ConfigureAwait(false);

    public static async Task<T> RunSerializedAsync<T>(string lane, string operationId, Func<YFinanceClient, CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
    {
        await ClientGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            TraceLog.InfoState("YFinanceRuntimeClientFactory", "SerializedClientEnter", [new("lane", lane), new("operation_id", operationId)]);
            return await action(GetSharedClient(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            TraceLog.WarnState("YFinanceRuntimeClientFactory", "SerializedClientError", [new("lane", lane), new("operation_id", operationId), new("message", ex.Message)]);
            throw;
        }
        finally
        {
            TraceLog.InfoState("YFinanceRuntimeClientFactory", "SerializedClientExit", [new("lane", lane), new("operation_id", operationId)]);
            ClientGate.Release();
        }
    }

    public static string CreateOperationId(string lane)
        => $"{lane}-{Interlocked.Increment(ref _operationSequence):D8}";
}
