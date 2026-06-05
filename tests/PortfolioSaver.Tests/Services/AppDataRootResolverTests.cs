using PortfolioSaver.Shared.Helpers;
using Xunit;

namespace PortfolioSaver.Tests.Services;

[Collection("EnvironmentSerial")]
public sealed class AppDataRootResolverTests
{
    [Fact]
    public void ResolveInstalledLocalDataRoot_ProductOverrideWinsOverLegacyAliases()
    {
        using EnvironmentScope scope = new();
        string productRoot = NewTempRoot("product");
        string legacyLocalRoot = NewTempRoot("legacy-local");
        string legacyAppRoot = NewTempRoot("legacy-app");
        scope.Set(AppDataRootResolver.ProductLocalDataRootEnvironmentVariable, productRoot);
        scope.Set(AppDataRootResolver.LegacyLocalDataRootEnvironmentVariable, legacyLocalRoot);
        scope.Set(AppDataRootResolver.LegacyAppDataRootEnvironmentVariable, legacyAppRoot);

        Assert.Equal(Path.GetFullPath(productRoot), AppDataRootResolver.ResolveInstalledLocalDataRoot(createDirectory: false));
    }

    [Fact]
    public void ResolveInstalledLocalDataRoot_LegacyLocalAliasWinsOverLegacyAppDataAlias()
    {
        using EnvironmentScope scope = new();
        string legacyLocalRoot = NewTempRoot("legacy-local");
        string legacyAppRoot = NewTempRoot("legacy-app");
        scope.Set(AppDataRootResolver.ProductLocalDataRootEnvironmentVariable, null);
        scope.Set(AppDataRootResolver.LegacyLocalDataRootEnvironmentVariable, legacyLocalRoot);
        scope.Set(AppDataRootResolver.LegacyAppDataRootEnvironmentVariable, legacyAppRoot);

        Assert.Equal(Path.GetFullPath(legacyLocalRoot), AppDataRootResolver.ResolveInstalledLocalDataRoot(createDirectory: false));
    }

    [Fact]
    public void ResolveInstalledLocalDataRoot_CreateDirectoryFalse_DoesNotCreateOverrideRoot()
    {
        using EnvironmentScope scope = new();
        string productRoot = NewTempRoot("product");
        scope.Set(AppDataRootResolver.ProductLocalDataRootEnvironmentVariable, productRoot);

        string resolved = AppDataRootResolver.ResolveInstalledLocalDataRoot(createDirectory: false);

        Assert.Equal(Path.GetFullPath(productRoot), resolved);
        Assert.False(Directory.Exists(productRoot));
    }

    [Fact]
    public void ResolveInstalledLocalDataRoot_CreateDirectoryTrue_CreatesOverrideRoot()
    {
        using EnvironmentScope scope = new();
        string productRoot = NewTempRoot("product");
        scope.Set(AppDataRootResolver.ProductLocalDataRootEnvironmentVariable, productRoot);

        try
        {
            string resolved = AppDataRootResolver.ResolveInstalledLocalDataRoot();

            Assert.Equal(Path.GetFullPath(productRoot), resolved);
            Assert.True(Directory.Exists(productRoot));
        }
        finally
        {
            if (Directory.Exists(productRoot))
                Directory.Delete(productRoot, recursive: true);
        }
    }

    [Fact]
    public void TryCopyLegacyRootOnce_IsIdempotentAndDoesNotOverwriteProductFiles()
    {
        string root = Path.Combine(Path.GetTempPath(), "PortfolioSaverTests", Guid.NewGuid().ToString("N"));
        string legacyRoot = Path.Combine(root, "PortfolioSaver");
        string productRoot = Path.Combine(root, AppDataRootResolver.AppLocalDataFolderName);
        try
        {
            Directory.CreateDirectory(legacyRoot);
            Directory.CreateDirectory(productRoot);
            File.WriteAllText(Path.Combine(legacyRoot, "settings.json"), "legacy");
            File.WriteAllText(Path.Combine(productRoot, "settings.json"), "product");

            AppDataRootResolver.TryCopyLegacyRootOnce(legacyRoot, productRoot);
            AppDataRootResolver.TryCopyLegacyRootOnce(legacyRoot, productRoot);

            Assert.Equal("product", File.ReadAllText(Path.Combine(productRoot, "settings.json")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TryCopyLegacyRootOnce_WritesSentinelAndSkipsLaterMigration()
    {
        string root = Path.Combine(Path.GetTempPath(), "PortfolioSaverTests", Guid.NewGuid().ToString("N"));
        string legacyRoot = Path.Combine(root, "PortfolioSaver");
        string productRoot = Path.Combine(root, AppDataRootResolver.AppLocalDataFolderName);
        try
        {
            Directory.CreateDirectory(legacyRoot);
            File.WriteAllText(Path.Combine(legacyRoot, "first.txt"), "first");

            AppDataRootResolver.TryCopyLegacyRootOnce(legacyRoot, productRoot);

            File.WriteAllText(Path.Combine(legacyRoot, "second.txt"), "second");
            AppDataRootResolver.TryCopyLegacyRootOnce(legacyRoot, productRoot);

            Assert.Equal("first", File.ReadAllText(Path.Combine(productRoot, "first.txt")));
            Assert.False(File.Exists(Path.Combine(productRoot, "second.txt")));
            Assert.True(File.Exists(Path.Combine(productRoot, ".portfolio-visualizer-migration-complete")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static string NewTempRoot(string suffix)
        => Path.Combine(Path.GetTempPath(), "PortfolioSaverTests", Guid.NewGuid().ToString("N"), suffix);

    private sealed class EnvironmentScope : IDisposable
    {
        private readonly Dictionary<string, string?> _previous = new(StringComparer.Ordinal);

        public void Set(string name, string? value)
        {
            if (!_previous.ContainsKey(name))
                _previous[name] = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process);

            Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.Process);
        }

        public void Dispose()
        {
            foreach (KeyValuePair<string, string?> pair in _previous)
                Environment.SetEnvironmentVariable(pair.Key, pair.Value, EnvironmentVariableTarget.Process);
        }
    }
}
