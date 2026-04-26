using System.IO;
using System.Diagnostics;
using System.Windows;
using PortfolioSaver.Screensaver.Services;
using PortfolioSaver.Screensaver.Windows;
using PortfolioSaver.Shared.Diagnostics;
using PortfolioSaver.Shared;

namespace PortfolioSaver.Screensaver;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        ScreensaverArgumentParser parser = new();
        ScreensaverLaunchArguments launchArguments = parser.Parse(args);

        if (launchArguments.Mode == ScreensaverMode.Configure)
        {
            try
            {
                string? configExe = ResolveConfigExecutable();
                TraceLog.Info("Screensaver.Program", $"Configure mode args=[{string.Join(", ", args)}], resolved config executable: {configExe ?? "<null>"}");

                if (!string.IsNullOrWhiteSpace(configExe) && File.Exists(configExe))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = configExe,
                        WorkingDirectory = Path.GetDirectoryName(configExe) ?? AppContext.BaseDirectory,
                        UseShellExecute = false
                    });
                    TraceLog.Info("Screensaver.Program", "Settings launch requested successfully.");
                }
                else
                {
                    TraceLog.Warn("Screensaver.Program", "Settings executable was not found.");
                    MessageBox.Show(
                        $"The {AppIdentity.ApplicationName} settings app could not be found next to the screensaver installation.",
                        AppIdentity.ApplicationName,
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                TraceLog.Error("Screensaver.Program", "Settings launch failed.", ex);
                MessageBox.Show(
                    $"The {AppIdentity.ApplicationName} settings app could not be started.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                    AppIdentity.ApplicationName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            return;
        }

        App app = new();
        Window window = launchArguments.Mode == ScreensaverMode.Preview
            ? new PreviewHostWindow(launchArguments.PreviewHandle)
            : new FullScreenHostWindow();
        app.Run(window);
    }

    private static string? ResolveConfigExecutable()
    {
        string executableDirectory = GetExecutableDirectory();
        string direct = Path.Combine(executableDirectory, "PortfolioSaver.Config.exe");
        if (File.Exists(direct))
            return direct;

        string? current = executableDirectory;
        for (int i = 0; i < 6 && !string.IsNullOrWhiteSpace(current); i++)
        {
            string siblingCandidate = Path.GetFullPath(Path.Combine(current, "..", "..", "..", "..", "PortfolioSaver.Config", "bin"));
            if (Directory.Exists(siblingCandidate))
            {
                string? match = Directory.EnumerateFiles(siblingCandidate, "PortfolioSaver.Config.exe", SearchOption.AllDirectories)
                    .FirstOrDefault(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(match))
                    return Path.GetFullPath(match);
            }

            current = Directory.GetParent(current)?.FullName;
        }

        return null;
    }

    private static string GetExecutableDirectory()
    {
        string? processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            string? processDirectory = Path.GetDirectoryName(processPath);
            if (!string.IsNullOrWhiteSpace(processDirectory))
                return processDirectory;
        }

        return AppContext.BaseDirectory;
    }

}
