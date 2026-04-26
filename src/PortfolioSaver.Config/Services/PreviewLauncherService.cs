using System.IO;

namespace PortfolioSaver.Config.Services;

public sealed class PreviewLauncherService
{
    public void LaunchPreview()
    {
        string screensaverExe = Path.Combine(AppContext.BaseDirectory, "PortfolioSaver.Screensaver.exe");
        if (!File.Exists(screensaverExe))
            return;

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = screensaverExe,
            Arguments = "/s",
            UseShellExecute = true
        });
    }
}
