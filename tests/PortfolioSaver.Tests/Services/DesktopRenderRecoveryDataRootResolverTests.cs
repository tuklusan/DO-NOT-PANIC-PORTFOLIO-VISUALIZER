// ============================================================================
// Copyright (c) 2026 Supratim Sanyal of SANYALnet Labs.
// Proprietary rights reserved except as expressly licensed herein.
//
// DO NOT PANIC PORTFOLIO VISUALIZER
// This file is governed by the SANYALnet Labs Non-Commercial License in the
// root LICENSE file. Non-Commercial use is permitted; Commercial Use and use
// for AI/ML model training are prohibited unless separately authorized.
//
// Attribution is required: "Based on original work by Supratim Sanyal of
// SANYALnet Labs." See LICENSE for full terms, warranty disclaimer, termination,
// patent, trademark, and governing-law provisions.
// ============================================================================
using PortfolioSaver.Shared.Diagnostics;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class DesktopRenderRecoveryDataRootResolverTests
{
    [Fact]
    public void Resolve_PrefersProductEnvironmentRootOverLegacyAliases()
    {
        string root = CreateTemporaryRoot();
        try
        {
            string productRoot = Path.Combine(root, "product");
            string legacyRoot = Path.Combine(root, "legacy");

            DesktopRenderRecoveryDataRoot resolved = DesktopRenderRecoveryDataRootResolver.Resolve(
                name => name switch
                {
                    DesktopRenderRecoveryDataRootResolver.ProductLocalDataRootEnvironmentVariable => productRoot,
                    DesktopRenderRecoveryDataRootResolver.LegacyLocalDataRootEnvironmentVariable => legacyRoot,
                    _ => null
                },
                _ => Path.Combine(root, "local"),
                () => Path.Combine(root, "temp"),
                Directory.CreateDirectory);

            Assert.Equal(Path.GetFullPath(productRoot), resolved.Root);
            Assert.Equal($"environment:{DesktopRenderRecoveryDataRootResolver.ProductLocalDataRootEnvironmentVariable}", resolved.Source);
            Assert.True(resolved.DirectoryReady);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public void Resolve_UsesLocalAppDataWhenNoEnvironmentRootExists()
    {
        string root = CreateTemporaryRoot();
        try
        {
            string localAppData = Path.Combine(root, "local");

            DesktopRenderRecoveryDataRoot resolved = DesktopRenderRecoveryDataRootResolver.Resolve(
                _ => null,
                _ => localAppData,
                () => Path.Combine(root, "temp"),
                Directory.CreateDirectory);

            Assert.Equal(
                Path.Combine(localAppData, DesktopRenderRecoveryDataRootResolver.AppLocalDataFolderName),
                resolved.Root);
            Assert.Equal("local_app_data", resolved.Source);
            Assert.True(resolved.DirectoryReady);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public void Resolve_FallsBackWhenEnvironmentRootCannotBeCreated()
    {
        string root = CreateTemporaryRoot();
        try
        {
            string badRoot = Path.Combine(root, "bad");
            string localAppData = Path.Combine(root, "local");
            List<string> warnings = [];

            DesktopRenderRecoveryDataRoot resolved = DesktopRenderRecoveryDataRootResolver.Resolve(
                name => string.Equals(name, DesktopRenderRecoveryDataRootResolver.ProductLocalDataRootEnvironmentVariable, StringComparison.Ordinal) ? badRoot : null,
                _ => localAppData,
                () => Path.Combine(root, "temp"),
                path =>
                {
                    if (string.Equals(path, Path.GetFullPath(badRoot), StringComparison.OrdinalIgnoreCase))
                        throw new UnauthorizedAccessException("blocked");

                    return Directory.CreateDirectory(path);
                },
                warnings.Add);

            Assert.Equal(
                Path.Combine(localAppData, DesktopRenderRecoveryDataRootResolver.AppLocalDataFolderName),
                resolved.Root);
            Assert.Equal("local_app_data", resolved.Source);
            Assert.Contains(warnings, warning => warning.Contains("environment", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public void Resolve_ReturnsAbsoluteLastResortWhenAllWritableRootsFail()
    {
        List<string> warnings = [];

        DesktopRenderRecoveryDataRoot resolved = DesktopRenderRecoveryDataRootResolver.Resolve(
            _ => null,
            _ => throw new UnauthorizedAccessException("no local appdata"),
            () => throw new UnauthorizedAccessException("no temp"),
            _ => throw new UnauthorizedAccessException("no create"),
            warnings.Add);

        Assert.True(Path.IsPathFullyQualified(resolved.Root));
        Assert.EndsWith(DesktopRenderRecoveryDataRootResolver.AppLocalDataFolderName, resolved.Root, StringComparison.Ordinal);
        Assert.Equal("absolute_last_resort", resolved.Source);
        Assert.False(resolved.DirectoryReady);
        Assert.NotEmpty(warnings);
    }

    private static string CreateTemporaryRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "PortfolioSaverTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTemporaryRoot(string root)
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
}
