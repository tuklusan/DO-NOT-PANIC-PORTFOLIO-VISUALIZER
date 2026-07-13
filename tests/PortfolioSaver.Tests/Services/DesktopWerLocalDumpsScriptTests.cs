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

public sealed class DesktopWerLocalDumpsScriptTests
{
    [Fact]
    public void ScriptTargetsOnlyDesktopExecutableUnderCurrentUserLocalDumps()
    {
        string script = ReadScript();

        Assert.Contains("'PortfolioSaver.Desktop.exe'", script, StringComparison.Ordinal);
        Assert.Contains(@"HKCU:\Software\Microsoft\Windows\Windows Error Reporting\LocalDumps", script, StringComparison.Ordinal);
        Assert.DoesNotContain("HKLM:", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("YFinance.NET.Server.exe", script, StringComparison.Ordinal);
    }

    [Fact]
    public void EnableRequiresPrivateDumpAcknowledgement()
    {
        string script = ReadScript();

        Assert.Contains("AcknowledgePrivateDumpContents", script, StringComparison.Ordinal);
        Assert.Contains("Enable requires -AcknowledgePrivateDumpContents", script, StringComparison.Ordinal);
        Assert.Contains("private process memory", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ScriptCanEnableShowAndDisableLocalDumps()
    {
        string script = ReadScript();

        Assert.Contains("[ValidateSet('Enable', 'Disable', 'Show')]", script, StringComparison.Ordinal);
        Assert.Contains("New-ItemProperty -LiteralPath $appDumpKey -Name DumpFolder", script, StringComparison.Ordinal);
        Assert.Contains("New-ItemProperty -LiteralPath $appDumpKey -Name DumpCount", script, StringComparison.Ordinal);
        Assert.Contains("New-ItemProperty -LiteralPath $appDumpKey -Name DumpType", script, StringComparison.Ordinal);
        Assert.Contains("Remove-Item -LiteralPath $appDumpKey", script, StringComparison.Ordinal);
    }

    private static string ReadScript()
    {
        string repoRoot = FindRepoRoot();
        return File.ReadAllText(Path.Combine(repoRoot, "build", "diagnostics", "Set-DesktopWerLocalDumps.ps1"));
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "DoNotPanicPortfolioVisualizer.sln")))
                return current.FullName;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("DoNotPanicPortfolioVisualizer.sln not found from test base directory.");
    }
}
