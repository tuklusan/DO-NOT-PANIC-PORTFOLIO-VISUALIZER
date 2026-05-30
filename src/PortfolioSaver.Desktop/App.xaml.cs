using System.Windows;
using PortfolioSaver.Shared.Diagnostics;
using PortfolioSaver.Shared.Integrity;
using PortfolioSaver.Shared.Services;

namespace PortfolioSaver.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        if (!ReleaseManifestGuard.ValidateCurrentExecutable("Desktop.App", out string integritySummary))
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

        DispatcherUnhandledException += (_, args) =>
        {
            TraceLog.Error("Desktop.App", "DispatcherUnhandledException", args.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            TraceLog.Error("Desktop.App", $"UnhandledException: {args.ExceptionObject}");
        };

        TraceLog.Info("Desktop.App", $"Startup args: {string.Join(" ", e.Args)}");
        YFinanceServerProcessManager.EnsureOwnedServerAsync("PortfolioSaver.Desktop").GetAwaiter().GetResult();
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            YFinanceServerProcessManager.StopOwnedServerAsync().GetAwaiter().GetResult();
        }
        finally
        {
            base.OnExit(e);
        }
    }
}
