namespace PortfolioSaver.Screensaver.Services;

public enum ScreensaverMode
{
    Configure,
    Preview,
    Fullscreen
}

public sealed class ScreensaverLaunchArguments
{
    public ScreensaverMode Mode { get; set; }
    public IntPtr PreviewHandle { get; set; }
}

public sealed class ScreensaverArgumentParser
{
    public ScreensaverLaunchArguments Parse(string[] args)
    {
        if (args.Length == 0)
            return new ScreensaverLaunchArguments { Mode = ScreensaverMode.Configure };

        string first = args[0].Trim().ToLowerInvariant();
        if (first.StartsWith("/s") || first.StartsWith("-s"))
            return new ScreensaverLaunchArguments { Mode = ScreensaverMode.Fullscreen };

        if (first.StartsWith("/p") || first.StartsWith("-p"))
        {
            IntPtr handle = IntPtr.Zero;
            if (args.Length > 1 && long.TryParse(args[1], out long hwnd))
                handle = new IntPtr(hwnd);

            return new ScreensaverLaunchArguments { Mode = ScreensaverMode.Preview, PreviewHandle = handle };
        }

        return new ScreensaverLaunchArguments { Mode = ScreensaverMode.Configure };
    }
}
