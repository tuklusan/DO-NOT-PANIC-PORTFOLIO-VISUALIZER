namespace PortfolioSaver.Shared.Helpers;

public static class PathHelper
{
    public const string AppLocalDataFolderName = AppDataRootResolver.AppLocalDataFolderName;
    public const string ProductLocalDataRootEnvironmentVariable = AppDataRootResolver.ProductLocalDataRootEnvironmentVariable;
    public const string LegacyLocalDataRootEnvironmentVariable = AppDataRootResolver.LegacyLocalDataRootEnvironmentVariable;
    public const string LegacyAppDataRootEnvironmentVariable = AppDataRootResolver.LegacyAppDataRootEnvironmentVariable;

    public static string GetAppDataDirectory()
    {
        string path = ResolveInstalledDataDirectory();
        Directory.CreateDirectory(path);
        return path;
    }

    public static string GetLocalDataDirectory()
    {
        string path = ResolveDataDirectory(
            ProductLocalDataRootEnvironmentVariable,
            Environment.SpecialFolder.LocalApplicationData);
        Directory.CreateDirectory(path);
        return path;
    }

    private static string ResolveInstalledDataDirectory()
        => AppDataRootResolver.ResolveInstalledLocalDataRoot();

    private static string ResolveDataDirectory(string environmentVariableName, Environment.SpecialFolder fallbackFolder)
    {
        return fallbackFolder == Environment.SpecialFolder.LocalApplicationData
            ? AppDataRootResolver.ResolveInstalledLocalDataRoot()
            : Path.Combine(Environment.GetFolderPath(fallbackFolder), AppLocalDataFolderName);
    }
}
