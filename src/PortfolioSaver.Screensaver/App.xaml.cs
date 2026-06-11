using System.Windows;
using System.Windows.Media;
using PortfolioSaver.Shared.Diagnostics;
using PortfolioSaver.Shared.Integrity;
using PortfolioSaver.Shared.Services;

namespace PortfolioSaver.Screensaver;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        if (!ReleaseManifestGuard.ValidateCurrentExecutable("Screensaver.App", out string integritySummary))
        {
            MessageBox.Show(
                "Release integrity check failed. This build may be stale or corrupted." +
                Environment.NewLine + Environment.NewLine +
                integritySummary,
                "DO NOT PANIC PORTFOLIO VISUALIZER",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
            return;
        }

        if (TraceLog.ShouldForceSoftwareRendering())
        {
            RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            TraceLog.Info("Screensaver.App", "Software rendering enabled.");
        }

        DispatcherUnhandledException += (_, args) =>
        {
            TraceLog.Error("Screensaver.App", "DispatcherUnhandledException", args.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            TraceLog.Error("Screensaver.App", $"UnhandledException: {args.ExceptionObject}");
        };

        TraceLog.Info("Screensaver.App", $"Startup args: {string.Join(" ", e.Args)}");
        try
        {
            await YFinanceServerProcessManager.EnsureOwnedServerAsync("PortfolioSaver.Screensaver");
        }
        catch (Exception ex)
        {
            TraceLog.Error("Screensaver.App", "Owned YFinance server startup failed.", ex);
            Shutdown(-1);
            return;
        }

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        QueueOwnedServerShutdown();
        base.OnExit(e);
    }

    private static void QueueOwnedServerShutdown()
    {
        TraceLog.Info("Screensaver.App", "Queueing owned YFinance server shutdown.");
        _ = Task.Run(async () =>
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(1));
            try
            {
                await YFinanceServerProcessManager.StopOwnedServerAsync(timeout.Token).ConfigureAwait(false);
                TraceLog.Info("Screensaver.App", "Owned YFinance server shutdown completed.");
            }
            catch (OperationCanceledException)
            {
                TraceLog.Warn("Screensaver.App", "Owned YFinance server shutdown timed out; owned server will also exit when owner PID disappears.");
            }
            catch (Exception ex)
            {
                TraceLog.Error("Screensaver.App", "Owned YFinance server shutdown failed.", ex);
            }
        });
    }
}
