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
}
