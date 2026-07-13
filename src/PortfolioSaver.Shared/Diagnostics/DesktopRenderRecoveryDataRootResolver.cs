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
using System.Security;

namespace PortfolioSaver.Shared.Diagnostics;

public sealed record DesktopRenderRecoveryDataRoot(string Root, string Source, bool DirectoryReady);

public static class DesktopRenderRecoveryDataRootResolver
{
    public const string AppLocalDataFolderName = "DoNotPanicPortfolioVisualizer";
    public const string ProductLocalDataRootEnvironmentVariable = "DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT";
    public const string LegacyLocalDataRootEnvironmentVariable = "PORTFOLIOSAVER_LOCALDATA_ROOT";
    public const string LegacyAppDataRootEnvironmentVariable = "PORTFOLIOSAVER_APPDATA_ROOT";

    private static readonly string[] LocalDataRootEnvironmentVariables =
    [
        ProductLocalDataRootEnvironmentVariable,
        LegacyLocalDataRootEnvironmentVariable,
        LegacyAppDataRootEnvironmentVariable
    ];

    public static DesktopRenderRecoveryDataRoot Resolve(Action<string>? warningSink = null)
        => Resolve(
            Environment.GetEnvironmentVariable,
            folder => Environment.GetFolderPath(folder),
            Path.GetTempPath,
            Directory.CreateDirectory,
            warningSink);

    public static DesktopRenderRecoveryDataRoot Resolve(
        Func<string, string?> environment,
        Func<Environment.SpecialFolder, string> specialFolder,
        Func<string> tempPath,
        Func<string, DirectoryInfo> createDirectory,
        Action<string>? warningSink = null)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(specialFolder);
        ArgumentNullException.ThrowIfNull(tempPath);
        ArgumentNullException.ThrowIfNull(createDirectory);

        foreach (string environmentVariable in LocalDataRootEnvironmentVariables)
        {
            string? value;
            try
            {
                value = environment(environmentVariable);
            }
            catch (Exception ex) when (IsRecoverableFileSystemException(ex))
            {
                warningSink?.Invoke($"Render recovery data root could not read environment variable '{environmentVariable}': {ex.Message}");
                continue;
            }

            if (string.IsNullOrWhiteSpace(value))
                continue;

            DesktopRenderRecoveryDataRoot? environmentRoot = TryPrepareRoot(
                value,
                $"environment:{environmentVariable}",
                createDirectory,
                warningSink);
            if (environmentRoot is not null)
                return environmentRoot;
        }

        DesktopRenderRecoveryDataRoot? localAppDataRoot = TryResolveSpecialFolderRoot(
            specialFolder,
            Environment.SpecialFolder.LocalApplicationData,
            "local_app_data",
            createDirectory,
            warningSink);
        if (localAppDataRoot is not null)
            return localAppDataRoot;

        DesktopRenderRecoveryDataRoot? tempRoot = TryResolveTempRoot(tempPath, createDirectory, warningSink);
        if (tempRoot is not null)
            return tempRoot;

        DesktopRenderRecoveryDataRoot? currentDirectoryRoot = TryResolveCurrentDirectoryRoot(createDirectory, warningSink);
        if (currentDirectoryRoot is not null)
            return currentDirectoryRoot;

        DesktopRenderRecoveryDataRoot? appBaseRoot = TryResolveAppBaseDirectoryRoot(createDirectory, warningSink);
        if (appBaseRoot is not null)
            return appBaseRoot;

        string lastResortRoot = Path.Combine(AppContext.BaseDirectory, AppLocalDataFolderName);
        DesktopRenderRecoveryDataRoot? lastResort = TryPrepareRoot(
            lastResortRoot,
            "absolute_last_resort",
            createDirectory,
            warningSink);
        if (lastResort is not null)
            return lastResort;

        warningSink?.Invoke($"Render recovery data root fell back to an unverified absolute path '{lastResortRoot}' because no writable root was available.");
        return new DesktopRenderRecoveryDataRoot(lastResortRoot, "absolute_last_resort", DirectoryReady: false);
    }

    private static DesktopRenderRecoveryDataRoot? TryResolveSpecialFolderRoot(
        Func<Environment.SpecialFolder, string> specialFolder,
        Environment.SpecialFolder folder,
        string source,
        Func<string, DirectoryInfo> createDirectory,
        Action<string>? warningSink)
    {
        try
        {
            string folderPath = specialFolder(folder);
            if (string.IsNullOrWhiteSpace(folderPath))
                return null;

            return TryPrepareRoot(
                Path.Combine(folderPath, AppLocalDataFolderName),
                source,
                createDirectory,
                warningSink);
        }
        catch (Exception ex) when (IsRecoverableFileSystemException(ex))
        {
            warningSink?.Invoke($"Render recovery data root could not resolve {source}: {ex.Message}");
            return null;
        }
    }

    private static DesktopRenderRecoveryDataRoot? TryResolveTempRoot(
        Func<string> tempPath,
        Func<string, DirectoryInfo> createDirectory,
        Action<string>? warningSink)
    {
        try
        {
            string temp = tempPath();
            if (string.IsNullOrWhiteSpace(temp))
                return null;

            return TryPrepareRoot(
                Path.Combine(temp, AppLocalDataFolderName),
                "temp",
                createDirectory,
                warningSink);
        }
        catch (Exception ex) when (IsRecoverableFileSystemException(ex))
        {
            warningSink?.Invoke($"Render recovery data root could not resolve temp path: {ex.Message}");
            return null;
        }
    }

    private static DesktopRenderRecoveryDataRoot? TryResolveCurrentDirectoryRoot(
        Func<string, DirectoryInfo> createDirectory,
        Action<string>? warningSink)
    {
        try
        {
            string currentDirectory = Environment.CurrentDirectory;
            if (string.IsNullOrWhiteSpace(currentDirectory))
                return null;

            return TryPrepareRoot(
                Path.Combine(currentDirectory, AppLocalDataFolderName),
                "current_directory",
                createDirectory,
                warningSink);
        }
        catch (Exception ex) when (IsRecoverableFileSystemException(ex))
        {
            warningSink?.Invoke($"Render recovery data root could not resolve current directory: {ex.Message}");
            return null;
        }
    }

    private static DesktopRenderRecoveryDataRoot? TryResolveAppBaseDirectoryRoot(
        Func<string, DirectoryInfo> createDirectory,
        Action<string>? warningSink)
    {
        try
        {
            string baseDirectory = AppContext.BaseDirectory;
            if (string.IsNullOrWhiteSpace(baseDirectory))
                return null;

            return TryPrepareRoot(
                Path.Combine(baseDirectory, AppLocalDataFolderName),
                "app_base_directory",
                createDirectory,
                warningSink);
        }
        catch (Exception ex) when (IsRecoverableFileSystemException(ex))
        {
            warningSink?.Invoke($"Render recovery data root could not resolve app base directory: {ex.Message}");
            return null;
        }
    }

    private static DesktopRenderRecoveryDataRoot? TryPrepareRoot(
        string root,
        string source,
        Func<string, DirectoryInfo> createDirectory,
        Action<string>? warningSink)
    {
        try
        {
            string fullPath = Path.GetFullPath(root.Trim());
            createDirectory(fullPath);
            return new DesktopRenderRecoveryDataRoot(fullPath, source, DirectoryReady: true);
        }
        catch (Exception ex) when (IsRecoverableFileSystemException(ex))
        {
            warningSink?.Invoke($"Render recovery data root candidate '{source}' was unavailable: {ex.Message}");
            return null;
        }
    }

    private static bool IsRecoverableFileSystemException(Exception ex)
        => ex is IOException or UnauthorizedAccessException or NotSupportedException or SecurityException;
}
