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
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class VirusTotalReleaseReportScriptTests
{
    [Fact]
    public void Script_SubmitsOnlyPublicGithubDownloadUrl()
    {
        string script = ReadRepoText("build", "release", "Publish-VirusTotalReleaseReport.ps1");

        Assert.Contains("browser_download_url", script, StringComparison.Ordinal);
        Assert.Contains("Installer asset URL is not a public GitHub download URL", script, StringComparison.Ordinal);
        Assert.Contains("^https://github\\.com/.+/releases/download/.+", script, StringComparison.Ordinal);
        Assert.Contains("^https://api\\.github\\.com/", script, StringComparison.Ordinal);
        Assert.Contains("$VirusTotalBaseUri/urls", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_GuardsSecretFileAndDoesNotLogApiKey()
    {
        string script = ReadRepoText("build", "release", "Publish-VirusTotalReleaseReport.ps1");

        Assert.Contains("git check-ignore -q", script, StringComparison.Ordinal);
        Assert.Contains("refusing to read VirusTotal secrets", script, StringComparison.Ordinal);
        Assert.Contains("VIRUSTOTAL_API_KEY", script, StringComparison.Ordinal);
        Assert.Contains("VirusTotalApiKey", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Write-Information $apiKey", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Write-Host $apiKey", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_EnforcesVirusTotalQuotaAndDigestSafety()
    {
        string script = ReadRepoText("build", "release", "Publish-VirusTotalReleaseReport.ps1");

        Assert.Contains("[ValidateRange(20, 3600)]", script, StringComparison.Ordinal);
        Assert.Contains("$PollIntervalSeconds * $MaxPollAttempts -gt 1800", script, StringComparison.Ordinal);
        Assert.Contains("VirusTotal polling window must not exceed 30 minutes", script, StringComparison.Ordinal);
        Assert.Contains("[regex]::Match($digest", script, StringComparison.Ordinal);
        Assert.Contains("sha256:([a-f0-9]{64})", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_WritesTrackedAdvisoryReportUnderReleasesFolder()
    {
        string script = ReadRepoText("build", "release", "Publish-VirusTotalReleaseReport.ps1");

        Assert.Contains("Join-Path $repoRoot 'releases'", script, StringComparison.Ordinal);
        Assert.Contains("OutputDirectory must resolve under the repository releases directory", script, StringComparison.Ordinal);
        Assert.Contains("virustotal-advisory-report.md", script, StringComparison.Ordinal);
        Assert.Contains("VirusTotal Advisory Scan Report", script, StringComparison.Ordinal);
        Assert.Contains("not a warranty, certification, or guarantee", script, StringComparison.Ordinal);
    }

    private static string ReadRepoText(params string[] relativeParts)
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (File.Exists(Path.Combine(current, "PortfolioScreensaver.sln")))
            {
                return File.ReadAllText(Path.Combine(new[] { current }.Concat(relativeParts).ToArray()));
            }

            DirectoryInfo? parent = Directory.GetParent(current);
            if (parent is null)
            {
                break;
            }

            current = parent.FullName;
        }

        throw new InvalidOperationException("Repository root could not be found from test output directory.");
    }
}
