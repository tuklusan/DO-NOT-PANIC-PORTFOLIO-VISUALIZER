using System.Security.Cryptography;
using System.Text.Json;
using PortfolioSaver.Shared.Diagnostics;

namespace PortfolioSaver.Shared.Integrity;

public sealed record ReleaseManifestFileEntry(string Path, long SizeBytes, string Sha256);

public sealed record ReleaseManifestDocument(
    int SchemaVersion,
    string ProductName,
    string ProductVersion,
    string GeneratedUtc,
    IReadOnlyList<ReleaseManifestFileEntry> Files);

public sealed record ReleaseManifestValidationResult(bool IsValid, string Summary, IReadOnlyList<string> Errors);

public static class ReleaseManifestValidator
{
    public const string ManifestFileName = "release-manifest.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static ReleaseManifestValidationResult ValidateDirectory(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            return Invalid($"Release directory does not exist: {directoryPath}");

        string root = Path.GetFullPath(directoryPath);
        string manifestPath = Path.Combine(root, ManifestFileName);
        if (!File.Exists(manifestPath))
            return Invalid($"Release manifest not found: {manifestPath}");

        ReleaseManifestDocument? manifest;
        try
        {
            string json = File.ReadAllText(manifestPath);
            manifest = JsonSerializer.Deserialize<ReleaseManifestDocument>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            return Invalid($"Release manifest is unreadable: {ex.GetType().Name}: {ex.Message}");
        }

        if (manifest is null)
            return Invalid("Release manifest is empty.");
        if (manifest.Files is null || manifest.Files.Count == 0)
            return Invalid("Release manifest contains no files.");

        List<string> errors = [];
        string rootWithSlash = EnsureTrailingSeparator(root);

        foreach (ReleaseManifestFileEntry entry in manifest.Files)
        {
            if (string.IsNullOrWhiteSpace(entry.Path))
            {
                errors.Add("Manifest entry has an empty path.");
                continue;
            }

            string entryPath = entry.Path.Replace('/', Path.DirectorySeparatorChar);
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(Path.Combine(root, entryPath));
            }
            catch (Exception ex)
            {
                errors.Add($"Path is invalid for {entry.Path}: {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            if (!fullPath.StartsWith(rootWithSlash, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Path escapes release root: {entry.Path}");
                continue;
            }

            if (!File.Exists(fullPath))
            {
                errors.Add($"Missing file: {entry.Path}");
                continue;
            }

            FileInfo fileInfo = new(fullPath);
            if (fileInfo.Length != entry.SizeBytes)
            {
                errors.Add($"Size mismatch: {entry.Path} expected={entry.SizeBytes} actual={fileInfo.Length}");
                continue;
            }

            string actualHash = ComputeSha256Hex(fullPath);
            if (!string.Equals(actualHash, NormalizeHex(entry.Sha256), StringComparison.OrdinalIgnoreCase))
                errors.Add($"Checksum mismatch: {entry.Path}");
        }

        if (errors.Count > 0)
            return Invalid($"Release integrity validation failed ({errors.Count} issue(s)).", errors);

        string summary = $"Release integrity validation passed ({manifest.Files.Count} files, {manifest.ProductVersion}).";
        return new ReleaseManifestValidationResult(true, summary, []);
    }

    private static string ComputeSha256Hex(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using SHA256 sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string NormalizeHex(string value)
        => value.Replace("-", string.Empty, StringComparison.Ordinal).Trim().ToLowerInvariant();

    private static string EnsureTrailingSeparator(string path)
        => path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;

    private static ReleaseManifestValidationResult Invalid(string summary, IReadOnlyList<string>? errors = null)
        => new(false, summary, errors ?? [summary]);
}

public static class ReleaseManifestGuard
{
    public static void ValidateCurrentExecutableInBackground(string source, Action<string> onValidationFailed)
    {
#if DEBUG
        TraceLog.Info(source, "Release integrity validation skipped in DEBUG build.");
#else
        string bypass = (Environment.GetEnvironmentVariable("PORTFOLIOSAVER_SKIP_MANIFEST_VALIDATION") ?? string.Empty).Trim();
        if (string.Equals(bypass, "1", StringComparison.Ordinal))
        {
            TraceLog.Warn(source, "Release integrity validation bypassed by environment override.");
            return;
        }

        _ = Task.Run(() => ReleaseManifestValidator.ValidateDirectory(AppContext.BaseDirectory))
            .ContinueWith(
                task =>
                {
                    if (task.IsFaulted)
                    {
                        string message = $"Release integrity validation failed: {task.Exception?.GetBaseException().Message ?? "unknown error"}";
                        TraceLog.Error(source, message);
                        onValidationFailed(message);
                        return;
                    }

                    ReleaseManifestValidationResult result = task.Result;
                    if (result.IsValid)
                    {
                        TraceLog.Info(source, result.Summary);
                        return;
                    }

                    foreach (string error in result.Errors.Take(10))
                        TraceLog.Error(source, error);
                    onValidationFailed(result.Summary);
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
#endif
    }

    public static bool ValidateCurrentExecutable(string source, out string summary)
    {
#if DEBUG
        summary = "Release integrity validation skipped in DEBUG build.";
        return true;
#else
        string bypass = (Environment.GetEnvironmentVariable("PORTFOLIOSAVER_SKIP_MANIFEST_VALIDATION") ?? string.Empty).Trim();
        if (string.Equals(bypass, "1", StringComparison.Ordinal))
        {
            summary = "Release integrity validation bypassed by environment override.";
            TraceLog.Warn(source, summary);
            return true;
        }

        ReleaseManifestValidationResult result = ReleaseManifestValidator.ValidateDirectory(AppContext.BaseDirectory);
        summary = result.Summary;
        if (result.IsValid)
        {
            TraceLog.Info(source, summary);
            return true;
        }

        foreach (string error in result.Errors.Take(10))
            TraceLog.Error(source, error);
        return false;
#endif
    }
}
