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
    public void InnoInstaller_InitializesProductLocalAppDataForOptionalAiSettings()
    {
        string script = ReadRepoText("build", "installer", "DoNotPanicPortfolioVisualizer.iss");

        Assert.Contains("ExpandConstant('{localappdata}\\DoNotPanicPortfolioVisualizer')", script, StringComparison.Ordinal);
        Assert.Contains("$root = Join-Path $env:LOCALAPPDATA ''DoNotPanicPortfolioVisualizer''", script, StringComparison.Ordinal);
        Assert.Contains("provider-secrets.json", script, StringComparison.Ordinal);
        Assert.DoesNotContain("{appdata}\\DoNotPanicPortfolioVisualizer", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Uninstaller_RemovesOnlyOwnedProductLocalAppDataRoots()
    {
        string script = ReadRepoText("build", "installer", "Cleanup-DoNotPanicPortfolioVisualizer.ps1");

        Assert.Contains("Test-IsSafeProfileLocalAppDataRoot", script, StringComparison.Ordinal);
        Assert.Contains("Remove-OwnedLocalAppDataRoot", script, StringComparison.Ordinal);
        Assert.Contains("Join-Path $env:LOCALAPPDATA 'DoNotPanicPortfolioVisualizer'", script, StringComparison.Ordinal);
        Assert.Contains("AppData\\Local\\DoNotPanicPortfolioVisualizer", script, StringComparison.Ordinal);
        Assert.Contains("Skipping unsafe Local AppData cleanup path", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Remove-Item -LiteralPath $env:LOCALAPPDATA", script, StringComparison.Ordinal);
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
