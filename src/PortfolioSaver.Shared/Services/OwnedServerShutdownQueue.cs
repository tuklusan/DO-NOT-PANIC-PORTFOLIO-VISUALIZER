using PortfolioSaver.Shared.Diagnostics;

namespace PortfolioSaver.Shared.Services;

public static class OwnedServerShutdownQueue
{
    public static void QueueShutdown(string sourceName)
    {
        TraceLog.Info(sourceName, "Queueing owned YFinance server shutdown.");
        _ = Task.Run(async () =>
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(1));
            try
            {
                await YFinanceServerProcessManager.StopOwnedServerAsync(timeout.Token).ConfigureAwait(false);
                TraceLog.Info(sourceName, "Owned YFinance server shutdown completed.");
            }
            catch (OperationCanceledException)
            {
                TraceLog.Warn(sourceName, "Owned YFinance server shutdown timed out; owned server will also exit when owner PID disappears.");
            }
            catch (Exception ex)
            {
                TraceLog.Error(sourceName, "Owned YFinance server shutdown failed.", ex);
            }
        });
    }
}
