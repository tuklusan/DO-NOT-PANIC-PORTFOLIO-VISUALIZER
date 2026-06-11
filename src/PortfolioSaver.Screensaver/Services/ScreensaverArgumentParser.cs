using System.Globalization;

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

        string first = args[0].Trim();
        string normalized = first.ToLowerInvariant();
        string option = normalized;
        string? inlineValue = null;
        int separatorIndex = normalized.IndexOf(':', StringComparison.Ordinal);
        if (separatorIndex >= 0)
        {
            option = normalized[..separatorIndex];
            inlineValue = first[(separatorIndex + 1)..].Trim();
        }

        if (option is "/s" or "-s")
            return new ScreensaverLaunchArguments { Mode = ScreensaverMode.Fullscreen };

        if (option is "/p" or "-p")
        {
            string? handleText = !string.IsNullOrWhiteSpace(inlineValue)
                ? inlineValue
                : args.Length > 1 ? args[1].Trim() : null;

            return new ScreensaverLaunchArguments
            {
                Mode = ScreensaverMode.Preview,
                PreviewHandle = ParseHandle(handleText)
            };
        }

        if (option is "/c" or "-c" or "/showconfig" or "-showconfig" or "--showconfig")
            return new ScreensaverLaunchArguments { Mode = ScreensaverMode.Configure };

        return new ScreensaverLaunchArguments { Mode = ScreensaverMode.Configure };
    }

    private static IntPtr ParseHandle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return IntPtr.Zero;

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long hwnd))
            return new IntPtr(hwnd);

        string hexValue = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? value[2..]
            : value;

        return long.TryParse(hexValue, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out hwnd)
            ? new IntPtr(hwnd)
            : IntPtr.Zero;
    }
}
