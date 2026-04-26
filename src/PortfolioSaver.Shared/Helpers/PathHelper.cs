namespace PortfolioSaver.Shared.Helpers;

public static class PathHelper
{
    public static string GetAppDataDirectory()
    {
        string path = ResolveDataDirectory(
            "PORTFOLIOSAVER_APPDATA_ROOT",
            Environment.SpecialFolder.ApplicationData);
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

    private static string ResolveDataDirectory(string environmentVariableName, Environment.SpecialFolder fallbackFolder)
    {
        string? overrideRoot = Environment.GetEnvironmentVariable(environmentVariableName);
        if (!string.IsNullOrWhiteSpace(overrideRoot))
            return Path.GetFullPath(overrideRoot.Trim());

        return Path.Combine(Environment.GetFolderPath(fallbackFolder), "PortfolioSaver");
    }
}
