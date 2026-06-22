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
using System.Security.Cryptography;
using System.Text.Json;
using PortfolioSaver.Shared.Integrity;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class ReleaseManifestValidatorTests
{
    [Fact]
    public void ValidateDirectory_ReturnsValid_ForMatchingManifest()
    {
        string root = CreateTempDirectory();
        try
        {
            string alphaPath = Path.Combine(root, "alpha.txt");
            string betaDir = Path.Combine(root, "sub");
            string betaPath = Path.Combine(betaDir, "beta.txt");
            Directory.CreateDirectory(betaDir);
            File.WriteAllText(alphaPath, "alpha");
            File.WriteAllText(betaPath, "beta");

            WriteManifest(root, [alphaPath, betaPath]);

            ReleaseManifestValidationResult result = ReleaseManifestValidator.ValidateDirectory(root);
            Assert.True(result.IsValid);
            Assert.DoesNotContain("failed", result.Summary, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            SafeDelete(root);
        }
    }

    [Fact]
    public void ValidateDirectory_ReturnsInvalid_WhenManifestMissing()
    {
        string root = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "alpha.txt"), "alpha");
            ReleaseManifestValidationResult result = ReleaseManifestValidator.ValidateDirectory(root);
            Assert.False(result.IsValid);
            Assert.Contains("manifest", result.Summary, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            SafeDelete(root);
        }
    }

    [Fact]
    public void ValidateDirectory_ReturnsInvalid_WhenChecksumMismatch()
    {
        string root = CreateTempDirectory();
        try
        {
            string alphaPath = Path.Combine(root, "alpha.txt");
            File.WriteAllText(alphaPath, "alpha");
            WriteManifest(root, [alphaPath]);
            File.WriteAllText(alphaPath, "bravo");

            ReleaseManifestValidationResult result = ReleaseManifestValidator.ValidateDirectory(root);
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, error => error.Contains("Checksum mismatch", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            SafeDelete(root);
        }
    }

    [Fact]
    public void InteractiveApps_QueueReleaseManifestValidationInBackground()
    {
        string repoRoot = GetRepoRoot();
        foreach (string appPath in new[]
                 {
                     Path.Combine(repoRoot, "src", "PortfolioSaver.Desktop", "App.xaml.cs"),
                     Path.Combine(repoRoot, "src", "PortfolioSaver.Config", "App.xaml.cs"),
                     Path.Combine(repoRoot, "src", "PortfolioSaver.Screensaver", "App.xaml.cs")
                 })
        {
            string source = File.ReadAllText(appPath);

            Assert.Contains("QueueReleaseIntegrityValidation();", source, StringComparison.Ordinal);
            Assert.Contains("ReleaseManifestGuard.ValidateCurrentExecutableInBackground", source, StringComparison.Ordinal);
            Assert.DoesNotContain("if (!ReleaseManifestGuard.ValidateCurrentExecutable", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ReleaseManifestGuard_BackgroundApiQueuesFullDirectoryValidation()
    {
        string repoRoot = GetRepoRoot();
        string source = File.ReadAllText(Path.Combine(repoRoot, "src", "PortfolioSaver.Shared", "Integrity", "ReleaseManifestValidator.cs"));

        Assert.Contains("ValidateCurrentExecutableInBackground", source, StringComparison.Ordinal);
        Assert.Contains("Task.Run(() => ReleaseManifestValidator.ValidateDirectory(AppContext.BaseDirectory))", source, StringComparison.Ordinal);
        Assert.Contains("TryNotifyValidationFailed(source, onValidationFailed, result.Summary);", source, StringComparison.Ordinal);
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "PortfolioSaverTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteManifest(string root, IReadOnlyList<string> fullPaths)
    {
        List<object> files = [];
        foreach (string fullPath in fullPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            FileInfo fileInfo = new(fullPath);
            files.Add(new
            {
                path = Path.GetRelativePath(root, fullPath).Replace('\\', '/'),
                sizeBytes = fileInfo.Length,
                sha256 = ComputeSha256Hex(fullPath)
            });
        }

        var manifest = new
        {
            schemaVersion = 1,
            productName = "DO NOT PANIC PORTFOLIO VISUALIZER",
            productVersion = "test",
            generatedUtc = DateTimeOffset.UtcNow.ToString("o"),
            files
        };

        string manifestPath = Path.Combine(root, ReleaseManifestValidator.ManifestFileName);
        string json = JsonSerializer.Serialize(manifest);
        File.WriteAllText(manifestPath, json);
    }

    private static string ComputeSha256Hex(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using SHA256 sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void SafeDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch
        {
        }
    }

    private static string GetRepoRoot()
    {
        string directory = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (File.Exists(Path.Combine(directory, "PortfolioScreensaver.sln")))
                return directory;

            directory = Directory.GetParent(directory)?.FullName ?? string.Empty;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
