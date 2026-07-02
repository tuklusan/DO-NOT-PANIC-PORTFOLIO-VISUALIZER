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

    [Fact]
    public void Script_PostsPublicVirusTotalUrlCommentWithReleaseContext()
    {
        string script = ReadRepoText("build", "release", "Publish-VirusTotalReleaseReport.ps1");

        Assert.Contains("Publish-VirusTotalUrlComment", script, StringComparison.Ordinal);
        Assert.Contains("accepts either Body or JsonBody, not both", script, StringComparison.Ordinal);
        Assert.Contains("$VirusTotalBaseUri/urls/$UrlId/comments", script, StringComparison.Ordinal);
        Assert.Contains("type = 'comment'", script, StringComparison.Ordinal);
        Assert.Contains("New-VirusTotalReleaseCommentText", script, StringComparison.Ordinal);
        Assert.Contains("Download URL:", script, StringComparison.Ordinal);
        Assert.Contains("Release tag:", script, StringComparison.Ordinal);
        Assert.Contains("Installer SHA-256:", script, StringComparison.Ordinal);
        Assert.Contains("Limit-Text", script, StringComparison.Ordinal);
        Assert.Contains("GetByteCount", script, StringComparison.Ordinal);
        Assert.Contains("MaximumLength must be greater than the UTF-8 byte length", script, StringComparison.Ordinal);
        Assert.Contains("MaximumLength 1200", script, StringComparison.Ordinal);
        Assert.Contains("compact single-link provenance comment with tag", script, StringComparison.Ordinal);
        Assert.Contains("rejected longer multi-link", script, StringComparison.Ordinal);
        Assert.Contains("Release context comment status", script, StringComparison.Ordinal);
        Assert.Contains("AllowCommentFailure", script, StringComparison.Ordinal);
        Assert.Contains("RequireComment", script, StringComparison.Ordinal);
        Assert.Contains("[switch]$SkipComment", script, StringComparison.Ordinal);
        Assert.Contains("Cannot combine -SkipComment with -RequireComment.", script, StringComparison.Ordinal);
        Assert.Contains("if (-not $SkipComment)", script, StringComparison.Ordinal);
        Assert.Contains("Use Body for application/x-www-form-urlencoded", script, StringComparison.Ordinal);
        Assert.Contains("Test-VirusTotalRetryableFailure", script, StringComparison.Ordinal);
        Assert.Contains("HTTP 429\\b", script, StringComparison.Ordinal);
        Assert.Contains("HTTP 5\\d\\d\\b", script, StringComparison.Ordinal);
        Assert.Contains("HTTP 409\\b", script, StringComparison.Ordinal);
        Assert.Contains("already-existing", script, StringComparison.Ordinal);
        Assert.Contains("release context comment already exists", script, StringComparison.Ordinal);
        Assert.DoesNotContain("MaximumLength 7600", script, StringComparison.Ordinal);
        Assert.Contains("[int]$Attempts = 3", script, StringComparison.Ordinal);
        Assert.Contains("[int]$RetrySleepSeconds = 20", script, StringComparison.Ordinal);
        Assert.Contains("Start-Sleep -Seconds $RetrySleepSeconds", script, StringComparison.Ordinal);
        Assert.Contains("Pass -SkipComment for scan-only advisory runs", script, StringComparison.Ordinal);
        Assert.Contains("Text.StringBuilder", script, StringComparison.Ordinal);
        Assert.Contains("Limit-Text produced a result longer than MaximumLength", script, StringComparison.Ordinal);
        Assert.Contains("one extra API call per release run", script, StringComparison.Ordinal);
        Assert.Contains("quota-aware retries", script, StringComparison.Ordinal);
        Assert.Contains("Comment-post failure is recorded in the advisory report by default", script, StringComparison.Ordinal);
        Assert.Contains("MaximumLength -le $suffixBytes", script, StringComparison.Ordinal);
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
