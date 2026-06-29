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
