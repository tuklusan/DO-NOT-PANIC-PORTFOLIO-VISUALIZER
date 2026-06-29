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
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class LocalAppDataStorageScriptTests
{
    private static readonly Lazy<string> RepoRoot = new(FindRepoRoot);

    [Fact]
    public void Installer_InitializesProductLocalAppDataAndMigratesLegacyRoot()
    {
        string script = ReadRepoText("build", "installer", "Install-PortfolioSaverScreensaver.ps1");

        AssertLocalAppDataJoin(script, "$localDataRoot", "DoNotPanicPortfolioVisualizer");
        AssertLocalAppDataJoin(script, "$legacyLocalDataRoot", "PortfolioSaver");
        Assert.Contains("Copy-LegacyLocalData", script, StringComparison.Ordinal);
        Assert.Contains("-SourceRoot $legacyLocalDataRoot", script, StringComparison.Ordinal);
        Assert.Contains("-TargetRoot $localDataRoot", script, StringComparison.Ordinal);
        AssertManagedSubPath(script, "Trace");
        AssertManagedSubPath(script, "Backgrounds\\ExchangePhotoCache");
        AssertManagedSubPath(script, "Caches\\History");
        Assert.DoesNotContain("$env:APPDATA", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Uninstaller_RemovesOnlyManagedProductLocalAppDataAndPreservesLegacyRoot()
    {
        string script = ReadRepoText("build", "installer", "Uninstall-PortfolioSaverScreensaver.ps1");

        AssertLocalAppDataJoin(script, "$localDataRoot", "DoNotPanicPortfolioVisualizer");
        AssertLocalAppDataJoin(script, "$legacyLocalDataRoot", "PortfolioSaver");
        Assert.Contains("$managedBackgroundCache", script, StringComparison.Ordinal);
        Assert.Contains("$managedHistoryCache", script, StringComparison.Ordinal);
        AssertManagedSubPath(script, "Backgrounds\\ExchangePhotoCache");
        AssertManagedSubPath(script, "Caches\\History");
        Assert.Contains("Remove-ManagedDirectory -Path $managedBackgroundCache", script, StringComparison.Ordinal);
        Assert.Contains("Remove-ManagedDirectory -Path $managedHistoryCache", script, StringComparison.Ordinal);
        Assert.Contains("Remove-ManagedDirectory -Path $managedTraceRoot", script, StringComparison.Ordinal);
        Assert.Contains("Remove-EmptyManagedDirectory -Path (Join-Path $localDataRoot \"Backgrounds\")", script, StringComparison.Ordinal);
        Assert.Contains("Remove-EmptyManagedDirectory -Path (Join-Path $localDataRoot \"Caches\")", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Remove-ManagedDirectory -Path (Join-Path $localDataRoot \"Backgrounds\")", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Remove-ManagedDirectory -Path (Join-Path $localDataRoot \"Caches\")", script, StringComparison.Ordinal);
        Assert.Contains("Legacy local data preserved for safety: $legacyLocalDataRoot", script, StringComparison.Ordinal);
        Assert.DoesNotContain("CustomBackgroundImageFolder", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Remove-Item -LiteralPath $legacyLocalDataRoot", script, StringComparison.Ordinal);
        Assert.DoesNotContain("$env:APPDATA", script, StringComparison.Ordinal);
    }

    [Fact]
    public void VmHarness_DefaultTraceRootUsesProductLocalAppDataButRetainsLegacyFallbackOnly()
    {
        string script = ReadRepoText("build", "vm", "Guest-UxDeepExercise.ps1");

        Assert.Contains("Join-Path $env:LOCALAPPDATA 'DoNotPanicPortfolioVisualizer'", script, StringComparison.Ordinal);
        Assert.Contains("function Get-HarnessAppDataRoot", script, StringComparison.Ordinal);
        Assert.Contains("$primaryPath = Join-Path (Get-HarnessAppDataRoot) $RelativePath", script, StringComparison.Ordinal);
        Assert.Contains("Join-Path (Join-Path $env:LOCALAPPDATA 'PortfolioSaver') $RelativePath", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Join-Path $env:APPDATA 'DoNotPanicPortfolioVisualizer'", script, StringComparison.Ordinal);
    }

    // These are intentionally static script-contract guards. Runtime behavior is covered
    // by AppDataRootResolverTests, PathHelperTests, SettingsFileServiceTests, TraceLogTests,
    // YFinanceCircularTraceSinkTests, and ExchangePhotoCacheServiceTests.
    private static void AssertLocalAppDataJoin(string script, string variableName, string folderName)
    {
        Assert.Contains(variableName, script, StringComparison.Ordinal);
        Assert.Contains("$env:LOCALAPPDATA", script, StringComparison.Ordinal);
        Assert.Contains(folderName, script, StringComparison.Ordinal);
    }

    private static void AssertManagedSubPath(string script, string relativePath)
    {
        Assert.Contains("Join-Path $localDataRoot", script, StringComparison.Ordinal);
        Assert.Contains(relativePath, script, StringComparison.Ordinal);
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

        throw new InvalidOperationException($"Repository root was not found from '{AppContext.BaseDirectory}'.");
    }
}
