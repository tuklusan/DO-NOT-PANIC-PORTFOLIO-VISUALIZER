namespace PortfolioSaver.Shared.Helpers;

public static class PathHelper
{
    public static string GetAppDataDirectory()
    {
        string path = ResolveInstalledDataDirectory();
        Directory.CreateDirectory(path);
        return path;
    }

    public static string GetLocalDataDirectory()
    {
        string path = ResolveDataDirectory(
            "PORTFOLIOSAVER_LOCALDATA_ROOT",
            Environment.SpecialFolder.LocalApplicationData);
        Directory.CreateDirectory(path);
        return path;
    }

    private static string ResolveInstalledDataDirectory()
    {
        string? localOverride = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT");
        if (!string.IsNullOrWhiteSpace(localOverride))
            return Path.GetFullPath(localOverride.Trim());

        string? legacyOverride = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT");
        if (!string.IsNullOrWhiteSpace(legacyOverride))
            return Path.GetFullPath(legacyOverride.Trim());

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PortfolioSaver");
    }

    private static string ResolveDataDirectory(string environmentVariableName, Environment.SpecialFolder fallbackFolder)
    {
        string? overrideRoot = Environment.GetEnvironmentVariable(environmentVariableName);
        if (!string.IsNullOrWhiteSpace(overrideRoot))
            return Path.GetFullPath(overrideRoot.Trim());

        return Path.Combine(Environment.GetFolderPath(fallbackFolder), "PortfolioSaver");
    }
}
