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
using PortfolioSaver.Shared.Licensing;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class ProjectLicenseServiceTests
{
    [Fact]
    public void DirectoryBuildProps_PacksAndCopiesRootLicense()
    {
        string repoRoot = GetRepoRoot();
        string props = File.ReadAllText(Path.Combine(repoRoot, "Directory.Build.props"));
        string targets = File.ReadAllText(Path.Combine(repoRoot, "Directory.Build.targets"));
        string sharedProject = File.ReadAllText(Path.Combine(repoRoot, "src", "PortfolioSaver.Shared", "PortfolioSaver.Shared.csproj"));

        Assert.Contains("<PackageLicenseFile Condition=\"'$(PackageLicenseFile)' == ''\">LICENSE</PackageLicenseFile>", props, StringComparison.Ordinal);
        Assert.Contains("Include=\"$(MSBuildThisFileDirectory)LICENSE\"", props, StringComparison.Ordinal);
        Assert.Contains("Pack=\"true\"", props, StringComparison.Ordinal);
        Assert.Contains("PackagePath=\"\"", props, StringComparison.Ordinal);
        Assert.Contains("CopyToOutputDirectory=\"Never\"", props, StringComparison.Ordinal);
        Assert.Contains("CopyToPublishDirectory=\"Never\"", props, StringComparison.Ordinal);
        Assert.Contains("<TfmSpecificPackageFile Include=\"$(MSBuildThisFileDirectory)THIRD-PARTY-NOTICES.md\"", props, StringComparison.Ordinal);
        Assert.Contains("PackagePath=\"\"", props, StringComparison.Ordinal);
        Assert.Contains("<TfmSpecificPackageFile Include=\"$(MSBuildThisFileDirectory)THIRD-PARTY-LICENSES\\**\\*\"", props, StringComparison.Ordinal);
        Assert.Contains("PackagePath=\"THIRD-PARTY-LICENSES\\%(RecursiveDir)\"", props, StringComparison.Ordinal);
        Assert.Contains("CopyPortfolioSaverThirdPartyFilesToBuildOutput", targets, StringComparison.Ordinal);
        Assert.Contains("CopyPortfolioSaverThirdPartyFilesToPublishOutput", targets, StringComparison.Ordinal);
        Assert.Contains("'$(_IsPublishing)' != 'true'", targets, StringComparison.Ordinal);
        Assert.Contains("DestinationFiles=\"@(PortfolioSaverDistributionRootFile->'$(TargetDir)%(Filename)%(Extension)')\"", targets, StringComparison.Ordinal);
        Assert.Contains("DestinationFiles=\"@(PortfolioSaverDistributionLicenseFile->'$(TargetDir)THIRD-PARTY-LICENSES\\%(RecursiveDir)%(Filename)%(Extension)')\"", targets, StringComparison.Ordinal);
        Assert.Contains("DestinationFiles=\"@(PortfolioSaverDistributionRootFile->'$(PublishDir)%(Filename)%(Extension)')\"", targets, StringComparison.Ordinal);
        Assert.Contains("DestinationFiles=\"@(PortfolioSaverDistributionLicenseFile->'$(PublishDir)THIRD-PARTY-LICENSES\\%(RecursiveDir)%(Filename)%(Extension)')\"", targets, StringComparison.Ordinal);
        Assert.Contains("<EmbeddedResource Include=\"..\\..\\LICENSE\" LogicalName=\"PortfolioSaver.LICENSE\" />", sharedProject, StringComparison.Ordinal);
    }

    [Fact]
    public void StandaloneInstallerPayload_IncludesThirdPartyNotices()
    {
        string repoRoot = GetRepoRoot();
        string publishScript = File.ReadAllText(Path.Combine(repoRoot, "build", "publish-safe-temp.ps1"));
        string installerScript = File.ReadAllText(Path.Combine(repoRoot, "build", "publish-inno-installer.ps1"));

        Assert.Contains("THIRD-PARTY-NOTICES.md", publishScript, StringComparison.Ordinal);
        Assert.Contains("THIRD-PARTY-LICENSES", publishScript, StringComparison.Ordinal);
        Assert.Contains("'THIRD-PARTY-NOTICES.md'", installerScript, StringComparison.Ordinal);
        Assert.Contains("'THIRD-PARTY-LICENSES\\APACHE-2.0.txt'", installerScript, StringComparison.Ordinal);
    }

    [Fact]
    public void GetLicenseText_ReadsRootLicenseFile()
    {
        string repoRoot = GetRepoRoot();
        string expected = ReadNormalizedRootLicense(repoRoot);

        string actual = ProjectLicenseService.GetLicenseText(
            [Path.Combine(repoRoot, "src", "PortfolioSaver.Shared")],
            File.ReadAllText);

        Assert.Equal(expected, actual);
        Assert.Contains("Non-Commercial Restriction", actual, StringComparison.Ordinal);
        Assert.Contains("State of Delaware", actual, StringComparison.Ordinal);
    }

    [Fact]
    public void EmbeddedLicenseText_MatchesRootLicenseFile()
    {
        string expected = ReadNormalizedRootLicense(GetRepoRoot());

        Assert.Equal(expected, ProjectLicenseService.GetEmbeddedLicenseText());
    }

    [Fact]
    public void GetLicenseText_DefaultRuntimePathReturnsBundledLicense()
    {
        string text = ProjectLicenseService.GetLicenseText();

        Assert.Contains("Copyright (c) 2026 Supratim Sanyal of SANYALnet Labs", text, StringComparison.Ordinal);
        Assert.Contains("Non-Commercial Restriction", text, StringComparison.Ordinal);
    }

    [Fact]
    public void GetLicenseText_FallsBackWhenLicenseFileIsMissing()
    {
        string missingRoot = Path.Combine(Path.GetTempPath(), "dnppv-license-missing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(missingRoot);

        try
        {
            string text = ProjectLicenseService.GetLicenseText([missingRoot], File.ReadAllText);

            Assert.Contains("SANYALnet Labs. Proprietary rights reserved except as expressly licensed herein.", text, StringComparison.Ordinal);
            Assert.Contains("Non-Commercial Restriction", text, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(missingRoot, recursive: true);
        }
    }

    [Fact]
    public void GetLicenseText_FallsBackWhenLicenseFileCannotBeRead()
    {
        string repoRoot = GetRepoRoot();

        string text = ProjectLicenseService.GetLicenseText(
            [repoRoot],
            _ => throw new IOException("simulated read failure"));

        Assert.Contains("SANYALnet Labs. Proprietary rights reserved except as expressly licensed herein.", text, StringComparison.Ordinal);
        Assert.Contains("Non-Commercial Restriction", text, StringComparison.Ordinal);
    }

    [Fact]
    public void GetLicenseText_FallsBackWhenSearchPathIsInvalid()
    {
        string text = ProjectLicenseService.GetLicenseText(["\0"], File.ReadAllText);

        Assert.Contains("SANYALnet Labs. Proprietary rights reserved except as expressly licensed herein.", text, StringComparison.Ordinal);
        Assert.Contains("Non-Commercial Restriction", text, StringComparison.Ordinal);
    }

    [Fact]
    public void GetLicenseText_FindsLicenseInParentDirectoryWithinSearchLimit()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "dnppv-license-parent-" + Guid.NewGuid().ToString("N"));
        string nested = Path.Combine(tempRoot, "one", "two", "three");
        Directory.CreateDirectory(nested);

        try
        {
            File.WriteAllText(Path.Combine(tempRoot, ProjectLicenseService.LicenseFileName), "parent license");

            string text = ProjectLicenseService.GetLicenseText([nested], File.ReadAllText);

            Assert.Equal("parent license", text);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static string GetRepoRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, "DoNotPanicPortfolioVisualizer.sln");
            if (File.Exists(candidate))
                return current.FullName;

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from test base directory.");
    }

    private static string ReadNormalizedRootLicense(string repoRoot)
        => File.ReadAllText(Path.Combine(repoRoot, ProjectLicenseService.LicenseFileName))
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Trim();
}
