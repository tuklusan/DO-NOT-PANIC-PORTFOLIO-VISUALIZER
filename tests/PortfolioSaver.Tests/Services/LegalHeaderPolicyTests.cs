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

public sealed class LegalHeaderPolicyTests
{
    private static readonly HashSet<string> RequiredHeaderExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // JSON/YAML/config-data files are intentionally excluded because they cannot
        // reliably carry opaque comments without risking parser-specific breakage.
        ".cs",
        ".csproj",
        ".cmd",
        ".config",
        ".manifest",
        ".md",
        ".props",
        ".ps1",
        ".slnx",
        ".targets",
        ".wsb",
        ".xaml",
        ".xml",
    };

    [Fact]
    public void RepositoryTextSourcesCarryProjectLegalNotice()
    {
        string repoRoot = GetRepoRoot();
        List<string> missing = [];

        foreach (string path in Directory.EnumerateFiles(repoRoot, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(repoRoot, path).Replace('\\', '/');
            string extension = Path.GetExtension(path);
            if (!RequiredHeaderExtensions.Contains(extension) || IsExcluded(relative))
                continue;

            string prefix = File.ReadLines(path).Take(35).Aggregate(string.Empty, static (current, line) => current + line + "\n");
            if (!prefix.Contains("This file is governed by the SANYALnet Labs Non-Commercial License", StringComparison.Ordinal) ||
                !prefix.Contains("Commercial Use and use", StringComparison.Ordinal) ||
                !prefix.Contains("for AI/ML model training are prohibited", StringComparison.Ordinal))
            {
                missing.Add(relative);
            }
        }

        Assert.True(missing.Count == 0, "Missing project legal header: " + string.Join(", ", missing));
    }

    [Fact]
    public void ThirdPartyApacheLicenseRemainsUnprefixed()
    {
        string repoRoot = GetRepoRoot();
        string apacheLicense = File.ReadAllText(Path.Combine(repoRoot, "THIRD-PARTY-LICENSES", "APACHE-2.0.txt"));

        Assert.Contains("Apache License", apacheLicense, StringComparison.Ordinal);
        Assert.DoesNotContain("This file is governed by the SANYALnet Labs Non-Commercial License", apacheLicense, StringComparison.Ordinal);
    }

    private static bool IsExcluded(string relative)
    {
        string[] excludedPrefixes =
        [
            ".git/",
            ".vs/",
            "build/artifacts/",
            "build/deepseek-review/",
            "build/validation/artifacts/",
            "build/vm/artifacts/",
            "src/PortfolioSaver.Render/Assets/",
            "THIRD-PARTY-LICENSES/",
        ];

        if (excludedPrefixes.Any(prefix => relative.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            return true;

        if (relative.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
            relative.Contains("/obj/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string fileName = Path.GetFileName(relative);
        return fileName.Equals("LICENSE", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetRepoRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "PortfolioScreensaver.sln")))
                return current.FullName;

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from test base directory.");
    }
}
