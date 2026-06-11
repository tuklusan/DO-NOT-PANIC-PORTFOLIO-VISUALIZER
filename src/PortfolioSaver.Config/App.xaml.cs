using System.Windows;
using System.Windows.Media;
using PortfolioSaver.Config.Windows;
using PortfolioSaver.Shared.Diagnostics;
using PortfolioSaver.Shared.Integrity;
using PortfolioSaver.Shared.Services;

namespace PortfolioSaver.Config;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        // The configuration app is text-heavy, and VirtualBox/remote desktop GPU paths have
        // repeatedly corrupted first-paint text. Prefer correctness over GPU acceleration here.
        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        TraceLog.Info("Config.App", "Software rendering enabled.");

        DispatcherUnhandledException += (_, args) =>
        {
            TraceLog.Error("Config.App", "DispatcherUnhandledException", args.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            TraceLog.Error("Config.App", $"UnhandledException: {args.ExceptionObject}");
        };

        TraceLog.Info("Config.App", $"Startup args: {string.Join(" ", e.Args)}");
        QueueReleaseIntegrityValidation();
        try
        {
            await YFinanceServerProcessManager.EnsureOwnedServerAsync("PortfolioSaver.Config");
        }
        catch (Exception ex)
        {
            TraceLog.Error("Config.App", "Owned YFinance server startup failed.", ex);
            Shutdown(-1);
            return;
        }

        base.OnStartup(e);

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    private void QueueReleaseIntegrityValidation()
        => ReleaseManifestGuard.ValidateCurrentExecutableInBackground(
            "Config.App",
            integritySummary => Dispatcher.BeginInvoke(new Action(() =>
            {
                MessageBox.Show(
                    "Release integrity check failed. This build may be stale or corrupted." +
                    Environment.NewLine + Environment.NewLine +
                    integritySummary,
                    "DO NOT PANIC PORTFOLIO VISUALIZER",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown(-1);
            })));

    protected override void OnExit(ExitEventArgs e)
    {
        OwnedServerShutdownQueue.QueueShutdown("Config.App");
        base.OnExit(e);
    }
}
