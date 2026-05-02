using System.Windows;
using System.Windows.Media;
using PortfolioSaver.Config.Windows;
using PortfolioSaver.Shared.Diagnostics;
using PortfolioSaver.Shared.Integrity;

namespace PortfolioSaver.Config;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        if (!ReleaseManifestGuard.ValidateCurrentExecutable("Config.App", out string integritySummary))
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
        base.OnStartup(e);

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }
}
