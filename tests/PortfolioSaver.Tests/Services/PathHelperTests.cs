// ============================================================================
// Copyright (c) 2026 Supratim Sanyal of SANYALnet Labs.
// Proprietary rights reserved except as expressly licensed herein.
//
// DO NOT PANIC PORTFOLIO VIEWER
// This software and its derivatives are licensed for STRICTLY NON-COMMERCIAL,
// personal, educational, or hobbyist use only. Commercial exploitation,
// corporate internal operations, or AI model training are strictly forbidden.
//
// ATTRIBUTION & DEPENDENCIES: This application incorporates the YFinance library,
// which is licensed under the Apache License, Version 2.0. A copy of the Apache
// License is provided within the distribution environment.
//
// FINANCIAL DISCLAIMER: This software is a passive visualization tool only.
// It does not provide financial, investment, legal, or tax advice. All data
// calculation and scraping outputs are provided 'AS IS' with zero guarantee
// of real-time accuracy or upstream availability.
//
// This file is subject to the terms and conditions defined in the LICENSE
// file located in the root directory of this source code repository.
// Removal or modification of this legal notice constitutes copyright infringement.
// ============================================================================
using PortfolioSaver.Shared.Helpers;
using Xunit;

namespace PortfolioSaver.Tests.Services;

[Collection("EnvironmentSerial")]
public sealed class PathHelperTests
{
    [Fact]
    public void GetAppDataDirectory_DefaultsToLocalAppDataProductRoot()
    {
        string? previousProductOverride = Environment.GetEnvironmentVariable("DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT");
        string? previousLocalOverride = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT");
        string? previousLegacyOverride = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT");
        Environment.SetEnvironmentVariable("DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT", null);
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT", null);
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", null);

        try
        {
            string expected = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                PathHelper.AppLocalDataFolderName);

            Assert.Equal(expected, PathHelper.GetAppDataDirectory());
        }
        finally
        {
            Environment.SetEnvironmentVariable("DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT", previousProductOverride);
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT", previousLocalOverride);
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", previousLegacyOverride);
        }
    }

    [Fact]
    public void GetAppDataDirectory_PrefersProductOverrideOverLegacyAliases()
    {
        string? previousProductOverride = Environment.GetEnvironmentVariable("DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT");
        string? previousLocalOverride = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT");
        string? previousLegacyOverride = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT");
        string productOverride = Path.Combine(Path.GetTempPath(), "PortfolioSaverTests", Guid.NewGuid().ToString("N"), "product");
        string localOverride = Path.Combine(Path.GetTempPath(), "PortfolioSaverTests", Guid.NewGuid().ToString("N"), "local");
        string legacyOverride = Path.Combine(Path.GetTempPath(), "PortfolioSaverTests", Guid.NewGuid().ToString("N"), "legacy");
        Environment.SetEnvironmentVariable("DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT", productOverride);
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT", localOverride);
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", legacyOverride);

        try
        {
            Assert.Equal(Path.GetFullPath(productOverride), PathHelper.GetAppDataDirectory());
        }
        finally
        {
            Environment.SetEnvironmentVariable("DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT", previousProductOverride);
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT", previousLocalOverride);
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", previousLegacyOverride);
        }
    }

    [Fact]
    public void GetAppDataDirectory_KeepsLegacyLocalOverrideAlias()
    {
        string? previousProductOverride = Environment.GetEnvironmentVariable("DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT");
        string? previousLocalOverride = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT");
        string? previousLegacyOverride = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT");
        string localOverride = Path.Combine(Path.GetTempPath(), "PortfolioSaverTests", Guid.NewGuid().ToString("N"), "local");
        string legacyOverride = Path.Combine(Path.GetTempPath(), "PortfolioSaverTests", Guid.NewGuid().ToString("N"), "legacy");
        Environment.SetEnvironmentVariable("DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT", null);
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT", localOverride);
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", legacyOverride);

        try
        {
            Assert.Equal(Path.GetFullPath(localOverride), PathHelper.GetAppDataDirectory());
        }
        finally
        {
            Environment.SetEnvironmentVariable("DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT", previousProductOverride);
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT", previousLocalOverride);
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", previousLegacyOverride);
        }
    }

    [Fact]
    public void LegacyRootMigration_CopiesLegacyFilesWhenProductRootMissingOrEmpty()
    {
        string root = Path.Combine(Path.GetTempPath(), "PortfolioSaverTests", Guid.NewGuid().ToString("N"));
        string legacyRoot = Path.Combine(root, "PortfolioSaver");
        string productRoot = Path.Combine(root, PathHelper.AppLocalDataFolderName);
        string legacySettings = Path.Combine(legacyRoot, "settings.json");
        string nestedFile = Path.Combine(legacyRoot, "Trace", "trace.circular.log");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(nestedFile)!);
            File.WriteAllText(legacySettings, "{}");
            File.WriteAllText(nestedFile, "trace");

            AppDataRootResolver.TryCopyDirectory(legacyRoot, productRoot);

            Assert.Equal("{}", File.ReadAllText(Path.Combine(productRoot, "settings.json")));
            Assert.Equal("trace", File.ReadAllText(Path.Combine(productRoot, "Trace", "trace.circular.log")));

            Directory.Delete(productRoot, recursive: true);
            Directory.CreateDirectory(productRoot);

            AppDataRootResolver.TryCopyDirectory(legacyRoot, productRoot);

            Assert.True(File.Exists(Path.Combine(productRoot, "settings.json")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LegacyRootMigration_MergesMissingFilesWithoutOverwritingProductFiles()
    {
        string root = Path.Combine(Path.GetTempPath(), "PortfolioSaverTests", Guid.NewGuid().ToString("N"));
        string legacyRoot = Path.Combine(root, "PortfolioSaver");
        string productRoot = Path.Combine(root, PathHelper.AppLocalDataFolderName);
        string legacyTraceFile = Path.Combine(legacyRoot, "Trace", "trace.circular.log");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(legacyTraceFile)!);
            Directory.CreateDirectory(productRoot);
            File.WriteAllText(Path.Combine(legacyRoot, "settings.json"), "legacy");
            File.WriteAllText(legacyTraceFile, "trace");
            File.WriteAllText(Path.Combine(productRoot, "settings.json"), "product");

            AppDataRootResolver.TryCopyDirectory(legacyRoot, productRoot);

            Assert.Equal("product", File.ReadAllText(Path.Combine(productRoot, "settings.json")));
            Assert.Equal("trace", File.ReadAllText(Path.Combine(productRoot, "Trace", "trace.circular.log")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

}
