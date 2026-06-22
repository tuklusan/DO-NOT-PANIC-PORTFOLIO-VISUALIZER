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
            if (!prefix.Contains("Removal or modification of this legal notice constitutes copyright infringement.", StringComparison.Ordinal))
                missing.Add(relative);
        }

        Assert.True(missing.Count == 0, "Missing project legal header: " + string.Join(", ", missing));
    }

    [Fact]
    public void ThirdPartyApacheLicenseRemainsUnprefixed()
    {
        string repoRoot = GetRepoRoot();
        string apacheLicense = File.ReadAllText(Path.Combine(repoRoot, "THIRD-PARTY-LICENSES", "APACHE-2.0.txt"));

        Assert.Contains("Apache License", apacheLicense, StringComparison.Ordinal);
        Assert.DoesNotContain("Removal or modification of this legal notice", apacheLicense, StringComparison.Ordinal);
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
