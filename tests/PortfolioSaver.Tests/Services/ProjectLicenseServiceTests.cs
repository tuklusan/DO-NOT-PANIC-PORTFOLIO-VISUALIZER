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
        string sharedProject = File.ReadAllText(Path.Combine(repoRoot, "src", "PortfolioSaver.Shared", "PortfolioSaver.Shared.csproj"));

        Assert.Contains("<PackageLicenseFile Condition=\"'$(PackageLicenseFile)' == ''\">LICENSE</PackageLicenseFile>", props, StringComparison.Ordinal);
        Assert.Contains("Include=\"$(MSBuildThisFileDirectory)LICENSE\"", props, StringComparison.Ordinal);
        Assert.Contains("Pack=\"true\"", props, StringComparison.Ordinal);
        Assert.Contains("PackagePath=\"\"", props, StringComparison.Ordinal);
        Assert.Contains("CopyToOutputDirectory=\"PreserveNewest\"", props, StringComparison.Ordinal);
        Assert.Contains("CopyToPublishDirectory=\"PreserveNewest\"", props, StringComparison.Ordinal);
        Assert.Contains("<EmbeddedResource Include=\"..\\..\\LICENSE\" LogicalName=\"PortfolioSaver.LICENSE\" />", sharedProject, StringComparison.Ordinal);
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
        Assert.Contains("STRICTLY NON-COMMERCIAL", actual, StringComparison.Ordinal);
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
        Assert.Contains("STRICTLY NON-COMMERCIAL", text, StringComparison.Ordinal);
    }

    [Fact]
    public void GetLicenseText_FallsBackWhenLicenseFileIsMissing()
    {
        string missingRoot = Path.Combine(Path.GetTempPath(), "dnppv-license-missing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(missingRoot);

        try
        {
            string text = ProjectLicenseService.GetLicenseText([missingRoot], File.ReadAllText);

            Assert.Contains("SANYALnet Labs. All rights reserved.", text, StringComparison.Ordinal);
            Assert.Contains("STRICTLY NON-COMMERCIAL", text, StringComparison.Ordinal);
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

        Assert.Contains("SANYALnet Labs. All rights reserved.", text, StringComparison.Ordinal);
        Assert.Contains("STRICTLY NON-COMMERCIAL", text, StringComparison.Ordinal);
    }

    [Fact]
    public void GetLicenseText_FallsBackWhenSearchPathIsInvalid()
    {
        string text = ProjectLicenseService.GetLicenseText(["\0"], File.ReadAllText);

        Assert.Contains("SANYALnet Labs. All rights reserved.", text, StringComparison.Ordinal);
        Assert.Contains("STRICTLY NON-COMMERCIAL", text, StringComparison.Ordinal);
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
            string candidate = Path.Combine(current.FullName, "PortfolioScreensaver.sln");
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
