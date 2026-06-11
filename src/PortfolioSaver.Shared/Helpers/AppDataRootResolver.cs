using System.Collections.Concurrent;
using System.Diagnostics;

// YFinance.NET links this source file into its net10.0 assembly with a distinct
// namespace so it can share the storage-root contract without depending on the
// Windows-targeted PortfolioSaver.Shared assembly.
#if YFINANCE_EMBEDDED_APPDATA_RESOLVER
namespace YFinance.NET.Storage;
#else
namespace PortfolioSaver.Shared.Helpers;
#endif

public static class AppDataRootResolver
{
    public const string AppLocalDataFolderName = "DoNotPanicPortfolioVisualizer";
    public const string LegacyAppLocalDataFolderName = "PortfolioSaver";
    public const string ProductLocalDataRootEnvironmentVariable = "DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT";
    public const string LegacyLocalDataRootEnvironmentVariable = "PORTFOLIOSAVER_LOCALDATA_ROOT";
    public const string LegacyAppDataRootEnvironmentVariable = "PORTFOLIOSAVER_APPDATA_ROOT";
    private const string MigrationSentinelFileName = ".portfolio-visualizer-migration-complete";

    private static readonly object MigrationSync = new();
    private static readonly ConcurrentDictionary<string, byte> MigratedRootPairs = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, Lazy<Task>> ScheduledMigrationTasks = new(StringComparer.OrdinalIgnoreCase);

    public static string ResolveInstalledLocalDataRoot(bool createDirectory = true)
    {
        string? overrideRoot = ResolveFirstEnvironmentOverride(
            ProductLocalDataRootEnvironmentVariable,
            LegacyLocalDataRootEnvironmentVariable,
            LegacyAppDataRootEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overrideRoot))
            return NormalizeRoot(overrideRoot, createDirectory);

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string productRoot = Path.Combine(localAppData, AppLocalDataFolderName);
        if (createDirectory)
            _ = QueueLegacyRootMigrationForStartup(Path.Combine(localAppData, LegacyAppLocalDataFolderName), productRoot);

        return NormalizeRoot(productRoot, createDirectory);
    }

    public static string? ResolveFirstEnvironmentOverride(params string[] names)
    {
        foreach (string name in names)
        {
            foreach (EnvironmentVariableTarget target in new[] { EnvironmentVariableTarget.Process, EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine })
            {
                string? value = Environment.GetEnvironmentVariable(name, target);
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
        }

        return null;
    }

    public static void TryCopyLegacyRootOnce(string legacyRoot, string productRoot)
    {
        string migrationKey = $"{Path.GetFullPath(legacyRoot)}|{Path.GetFullPath(productRoot)}";
        lock (MigrationSync)
        {
            if (!MigratedRootPairs.TryAdd(migrationKey, 0))
                return;

            string sentinelPath = Path.Combine(productRoot, MigrationSentinelFileName);
            if (File.Exists(sentinelPath))
                return;

            TryCopyDirectory(legacyRoot, productRoot);
            TryWriteMigrationSentinel(legacyRoot, sentinelPath);
        }
    }

    internal static Task QueueLegacyRootMigrationForStartup(string legacyRoot, string productRoot)
    {
        string migrationKey = $"{Path.GetFullPath(legacyRoot)}|{Path.GetFullPath(productRoot)}";
        Lazy<Task> migration = ScheduledMigrationTasks.GetOrAdd(
            migrationKey,
            _ => new Lazy<Task>(
                () => Task.Run(() => TryCopyLegacyRootOnce(legacyRoot, productRoot)),
                LazyThreadSafetyMode.ExecutionAndPublication));

        return migration.Value;
    }

    public static void TryCopyDirectory(string sourceDirectory, string targetDirectory)
    {
        try
        {
            DirectoryInfo source = new(sourceDirectory);
            if (!source.Exists || IsReparsePoint(source.Attributes))
                return;

            Directory.CreateDirectory(targetDirectory);
            foreach (DirectoryInfo childDirectory in source.EnumerateDirectories())
            {
                if (IsReparsePoint(childDirectory.Attributes))
                    continue;

                TryCopyDirectory(childDirectory.FullName, Path.Combine(targetDirectory, childDirectory.Name));
            }

            foreach (FileInfo file in source.EnumerateFiles())
            {
                if (IsReparsePoint(file.Attributes))
                    continue;

                string targetFile = Path.Combine(targetDirectory, file.Name);
                if (File.Exists(targetFile))
                    continue;

                TryCopyFile(file.FullName, targetFile);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"Local data root migration skipped directory '{sourceDirectory}': {ex.Message}");
        }
    }

    private static void TryCopyFile(string sourceFile, string targetFile)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            File.Copy(sourceFile, targetFile, overwrite: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"Local data root migration skipped file '{sourceFile}': {ex.Message}");
        }
    }

    private static bool IsReparsePoint(FileAttributes attributes)
        => (attributes & FileAttributes.ReparsePoint) != 0;

    private static void TryWriteMigrationSentinel(string legacyRoot, string sentinelPath)
    {
        try
        {
            if (!Directory.Exists(legacyRoot))
                return;

            Directory.CreateDirectory(Path.GetDirectoryName(sentinelPath)!);
            File.WriteAllText(sentinelPath, DateTimeOffset.UtcNow.ToString("O"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"Local data root migration sentinel write skipped: {ex.Message}");
        }
    }

    private static string NormalizeRoot(string root, bool createDirectory)
    {
        string fullPath = Path.GetFullPath(root.Trim());
        if (createDirectory)
            Directory.CreateDirectory(fullPath);

        return fullPath;
    }
}
