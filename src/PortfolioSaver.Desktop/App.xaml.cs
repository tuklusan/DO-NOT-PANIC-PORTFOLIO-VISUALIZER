using System.Windows;
using System.Windows.Threading;
using PortfolioSaver.Shared.Diagnostics;
using PortfolioSaver.Shared.Integrity;
using PortfolioSaver.Shared.Services;
using PortfolioSaver.Desktop.Windows;

namespace PortfolioSaver.Desktop;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
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
        try
        {
            await YFinanceServerProcessManager.EnsureOwnedServerAsync("PortfolioSaver.Desktop");
        }
        catch (Exception ex)
        {
            TraceLog.Error("Desktop.App", "Owned YFinance server startup failed.", ex);
            Shutdown(-1);
            return;
        }

        bool startFullScreen = e.Args.Any(arg => string.Equals(arg, "--fullscreen", StringComparison.OrdinalIgnoreCase));
        base.OnStartup(e);

        var window = new MainWindow();
        if (startFullScreen)
        {
            window.Loaded += (_, _) =>
            {
                window.Dispatcher.BeginInvoke(
                    DispatcherPriority.ApplicationIdle,
                    new Action(window.EnterFullScreen));
            };
        }

        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        OwnedServerShutdownQueue.QueueShutdown("Desktop.App");
        base.OnExit(e);
    }
}
