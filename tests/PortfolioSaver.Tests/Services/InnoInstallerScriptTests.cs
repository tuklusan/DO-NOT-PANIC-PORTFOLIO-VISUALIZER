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
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class InnoInstallerScriptTests
{
    private static readonly Lazy<string> RepoRoot = new(FindRepoRoot);

    [Fact]
    public void InnoScript_RequiresLicenseAndElevatedProgramFilesInstall()
    {
        string script = ReadRepoText("build", "installer", "DoNotPanicPortfolioVisualizer.iss");

        Assert.Contains("PrivilegesRequired=admin", script, StringComparison.Ordinal);
        Assert.DoesNotContain("PrivilegesRequiredOverridesAllowed", script, StringComparison.Ordinal);
        Assert.Contains("#error AppVersion must be supplied by build/publish-inno-installer.ps1.", script, StringComparison.Ordinal);
        Assert.Contains("LicenseFile={#LicenseFile}", script, StringComparison.Ordinal);
        Assert.Contains(@"DefaultDirName={autopf}\{#AppPublisher}\{#AppFolderName}", script, StringComparison.Ordinal);
        Assert.Contains("#define AppPublisher \"SANYALnet Labs\"", script, StringComparison.Ordinal);
        Assert.Contains("ArchitecturesInstallIn64BitMode=x64compatible", script, StringComparison.Ordinal);
        Assert.Contains(@"Source: ""{#SourceRoot}\*""; DestDir: ""{app}""", script, StringComparison.Ordinal);
        Assert.Contains("Cleanup-DoNotPanicPortfolioVisualizer.ps1", script, StringComparison.Ordinal);
        Assert.Contains("-AllUsers", script, StringComparison.Ordinal);
        Assert.Contains("skipifdoesntexist", script, StringComparison.Ordinal);
        Assert.Contains("function InitializeUninstall(): Boolean;", script, StringComparison.Ordinal);
        Assert.Contains("if not UninstallSilent then", script, StringComparison.Ordinal);
        Assert.DoesNotContain("if not WizardSilent then", script, StringComparison.Ordinal);
        Assert.Contains("DoNotPanicPortfolioVisualizer for local Windows user profiles", script, StringComparison.Ordinal);

        string cycleScript = ReadRepoText("build", "installer", "Test-InnoInstallCycle.ps1");
        Assert.Contains("/SUPPRESSMSGBOXES", cycleScript, StringComparison.Ordinal);
        Assert.Contains("#requires -Version 7.0", cycleScript, StringComparison.Ordinal);
        Assert.Contains("ArgumentList.Add", cycleScript, StringComparison.Ordinal);
        Assert.Contains(@"HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{B0839D4C-1D29-4D9C-95E3-C88E4D8E37E5}_is1", cycleScript, StringComparison.Ordinal);
        Assert.Contains("Join-Path $repoRoot 'build\\validation\\artifacts\\inno-install-cycle'", cycleScript, StringComparison.Ordinal);
    }

    [Fact]
    public void InnoPublisher_UsesSafeTempPayloadAndSupportsPerUserIscc()
    {
        string script = ReadRepoText("build", "publish-inno-installer.ps1");

        Assert.Contains("publish-safe-temp.ps1", script, StringComparison.Ordinal);
        Assert.Contains("if (-not $?)", script, StringComparison.Ordinal);
        Assert.Contains("exit code $LASTEXITCODE", script, StringComparison.Ordinal);
        Assert.DoesNotContain("publish-safe-temp.ps1 failed with exit code $LASTEXITCODE", script, StringComparison.Ordinal);
        Assert.Contains(@"$env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'", script, StringComparison.Ordinal);
        Assert.Contains("local-name()=\"PortfolioSaverVersion\"", script, StringComparison.Ordinal);
        Assert.Contains("PortfolioSaver.Desktop.exe", script, StringComparison.Ordinal);
        Assert.Contains("PortfolioSaver.Config.exe", script, StringComparison.Ordinal);
        Assert.Contains("PortfolioSaver.Screensaver.scr", script, StringComparison.Ordinal);
        Assert.Contains("THIRD-PARTY-LICENSES\\APACHE-2.0.txt", script, StringComparison.Ordinal);
        Assert.Contains("$serverRoot = Join-Path $safeTempRoot 'server'", script, StringComparison.Ordinal);
        Assert.Contains("Copy-DirectoryContents -Source $serverRoot -Destination (Join-Path $payloadRoot 'YFinanceServer')", script, StringComparison.Ordinal);
        Assert.Contains("YFinanceServer\\YFinance.NET.Server.dll", script, StringComparison.Ordinal);
        Assert.Contains("/DLicenseFile=$licensePath", script, StringComparison.Ordinal);
        Assert.Contains("Release manifest generator not found", script, StringComparison.Ordinal);
        Assert.Contains("Safe-temp publish directory missing", script, StringComparison.Ordinal);
        Assert.Contains("release-manifest.json", script, StringComparison.Ordinal);
        Assert.Contains("Manifest generation failed for Inno payload", script, StringComparison.Ordinal);
        Assert.Contains("'*.pdb','*.nupkg'", script, StringComparison.Ordinal);
        Assert.DoesNotContain("'*.xml'", script, StringComparison.Ordinal);

        string safeTempScript = ReadRepoText("build", "publish-safe-temp.ps1");
        Assert.Contains("The Inno installer pipeline packages this canonical server publish", safeTempScript, StringComparison.Ordinal);
        Assert.Contains("$serverOut = Join-Path $publishRoot \"server\"", safeTempScript, StringComparison.Ordinal);
        Assert.Contains("$serverProject = \".\\YFinance.net\\YFinance.NET.Server\\YFinance.NET.Server.csproj\"", safeTempScript, StringComparison.Ordinal);
        Assert.Contains("Publishing YFinance server", safeTempScript, StringComparison.Ordinal);
        Assert.Contains("@{ From = $serverTempPublish; To = $serverOut }", safeTempScript, StringComparison.Ordinal);
    }

    [Fact]
    public void InnoCleanup_RemovesOnlyProductLocalAppDataRoots()
    {
        string script = ReadRepoText("build", "installer", "Cleanup-DoNotPanicPortfolioVisualizer.ps1");

        Assert.Contains("DoNotPanicPortfolioVisualizer", script, StringComparison.Ordinal);
        Assert.Contains("Test-IsSafeProfileLocalAppDataRoot", script, StringComparison.Ordinal);
        Assert.Contains("AppData\\Local\\DoNotPanicPortfolioVisualizer", script, StringComparison.Ordinal);
        Assert.Contains("[switch]$AllUsers", script, StringComparison.Ordinal);
        Assert.Contains("#requires -Version 5.1", script, StringComparison.Ordinal);
        Assert.Contains("CloseMainWindow", script, StringComparison.Ordinal);
        Assert.Contains("Get-CimInstance Win32_UserProfile", script, StringComparison.Ordinal);
        Assert.Contains("Could not enumerate all Windows user profiles for cleanup", script, StringComparison.Ordinal);
        Assert.DoesNotContain("SystemDrive 'Users'", script, StringComparison.Ordinal);
        Assert.Contains("*\\YFinanceServer\\YFinance.NET.Server.dll*", script, StringComparison.Ordinal);
        Assert.Contains("Invoke-CimMethod -InputObject $serverHost -MethodName Terminate", script, StringComparison.Ordinal);
        Assert.Contains("ReparsePoint", script, StringComparison.Ordinal);
        Assert.DoesNotContain("AppData\\Local\\PortfolioSaver", script, StringComparison.Ordinal);
        Assert.DoesNotContain("$env:APPDATA", script, StringComparison.Ordinal);
    }

    private static string ReadRepoText(params string[] relativeParts)
    {
        string path = Path.Combine(RepoRoot.Value, Path.Combine(relativeParts));
        return File.ReadAllText(path);
    }

    private static string FindRepoRoot()
    {
        string? overrideRoot = Environment.GetEnvironmentVariable("REPO_ROOT");
        if (!string.IsNullOrWhiteSpace(overrideRoot) && File.Exists(Path.Combine(overrideRoot, "PortfolioScreensaver.sln")))
            return Path.GetFullPath(overrideRoot);

        string? directory = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (File.Exists(Path.Combine(directory, "PortfolioScreensaver.sln")))
                return directory;

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new InvalidOperationException($"Repository root was not found from '{AppContext.BaseDirectory}'. Set REPO_ROOT to the repository root when running tests from a shadow-copy or detached output directory.");
    }
}
