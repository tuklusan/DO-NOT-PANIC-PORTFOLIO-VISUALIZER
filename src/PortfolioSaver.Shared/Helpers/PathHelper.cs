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
