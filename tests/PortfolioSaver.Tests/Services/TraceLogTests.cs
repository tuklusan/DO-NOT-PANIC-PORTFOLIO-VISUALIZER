using System.Text;
using System.Reflection;
using PortfolioSaver.Shared.Diagnostics;
using PortfolioSaver.Shared.Helpers;
using Xunit;

namespace PortfolioSaver.Tests.Services;

[Collection("EnvironmentSerial")]
public sealed class TraceLogTests
{
    [Fact]
    public async Task TraceLog_WritesToFourMegCircularFileUnderAppData()
    {
        string appDataRoot = Path.Combine(Path.GetTempPath(), "PortfolioSaverTraceTest", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", appDataRoot);
        try
        {
            string traceDirectory = Path.Combine(PathHelper.GetAppDataDirectory(), "Trace");
            string traceFilePath = Path.Combine(traceDirectory, "trace.circular.log");
            string traceIndexPath = Path.Combine(traceDirectory, "trace.circular.idx");
            Directory.CreateDirectory(traceDirectory);
            string marker = "trace-test-" + Guid.NewGuid().ToString("N");
            MethodInfo? writeCircularMethod = typeof(TraceLog).GetMethod(
                "WriteCircular",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(writeCircularMethod);

            string line = $"{DateTimeOffset.UtcNow:O} | INFO | program=PortfolioSaver.Tests | source=TraceLogTests | function=TraceLog_WritesToFourMegCircularFileUnderAppData | {marker}";
            writeCircularMethod!.Invoke(null, [line]);
            writeCircularMethod!.Invoke(null, [line]);
            writeCircularMethod!.Invoke(null, [line]);

            bool observed = await WaitForTraceAsync(
                traceFilePath,
                traceIndexPath,
                text =>
                {
                    if (!text.Contains(marker, StringComparison.Ordinal))
                        return false;

                    Assert.Contains("program=", text, StringComparison.Ordinal);
                    Assert.Contains("function=", text, StringComparison.Ordinal);
                    return true;
                });

            Assert.True(observed, "Trace marker was not observed in the 4MB circular trace file.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", null);
            DeleteDirectoryWithRetry(appDataRoot);
        }
    }

    [Fact]
    public void TraceLog_InfoState_FormatsStructuredFields()
    {
        string marker = "trace-state-" + Guid.NewGuid().ToString("N");
        MethodInfo formatter = typeof(TraceLog).GetMethod(
            "BuildStructuredMessage",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find TraceLog.BuildStructuredMessage.");

        string message = (string)(formatter.Invoke(
            null,
            [
                "StructuredTrace",
                new[]
                {
                    new KeyValuePair<string, object?>("marker", marker),
                    new KeyValuePair<string, object?>("symbols", new[] { "AAPL", "MSFT", "NVDA" }),
                    new KeyValuePair<string, object?>("remaining", 2)
                }
            ]) ?? throw new InvalidOperationException("Structured message formatter returned null."));

        Assert.Contains($"marker={marker}", message, StringComparison.Ordinal);
        Assert.Contains("event=StructuredTrace", message, StringComparison.Ordinal);
        Assert.Contains("symbols=[AAPL, MSFT, NVDA]", message, StringComparison.Ordinal);
        Assert.Contains("remaining=2", message, StringComparison.Ordinal);
    }

    [Fact]
    public void TraceLog_InfoState_RedactsSecretLikeStructuredFields()
    {
        MethodInfo formatter = typeof(TraceLog).GetMethod(
            "BuildStructuredMessage",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find TraceLog.BuildStructuredMessage.");

        string message = (string)(formatter.Invoke(
            null,
            [
                "SecretTrace",
                new[]
                {
                    new KeyValuePair<string, object?>("deepseek_api_key", "sk-live-secret"),
                    new KeyValuePair<string, object?>("Authorization", "Bearer abc123"),
                    new KeyValuePair<string, object?>("message", "token=abc123,def password:letmein safe=ok")
                }
            ]) ?? throw new InvalidOperationException("Structured message formatter returned null."));

        Assert.Contains("deepseek_api_key=<redacted>", message, StringComparison.Ordinal);
        Assert.Contains("Authorization=<redacted>", message, StringComparison.Ordinal);
        Assert.Contains("token=<redacted>", message, StringComparison.Ordinal);
        Assert.Contains("password:<redacted>", message, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-live-secret", message, StringComparison.Ordinal);
        Assert.DoesNotContain("abc123", message, StringComparison.Ordinal);
        Assert.DoesNotContain("def", message, StringComparison.Ordinal);
        Assert.DoesNotContain("letmein", message, StringComparison.Ordinal);
    }

    [Fact]
    public void TraceLog_NetworkMetadataResolution_IsNotPerformedByStaticInitializers()
    {
        string repoRoot = GetRepoRoot();
        string source = File.ReadAllText(Path.Combine(repoRoot, "src", "PortfolioSaver.Shared", "Diagnostics", "TraceLog.cs"));

        Assert.Contains("private static NetworkMetadata _networkMetadata = new(Environment.MachineName, \"127.0.0.1\");", source, StringComparison.Ordinal);
        Assert.Contains("EnsureNetworkMetadataResolution();", source, StringComparison.Ordinal);
        Assert.Contains("_ = Task.Run(ResolveNetworkMetadata);", source, StringComparison.Ordinal);
        Assert.Contains("NetworkMetadata metadata = Volatile.Read(ref _networkMetadata);", source, StringComparison.Ordinal);
        Assert.Contains("Volatile.Write(ref _networkMetadata, new NetworkMetadata(hostName, localIp));", source, StringComparison.Ordinal);
        Assert.Contains("private sealed record NetworkMetadata(string HostName, string LocalIp);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("static readonly string HostName = GetHostNameSafe()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("static readonly string LocalIp = GetPrimaryIpSafe()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TraceLog_CircularIndexPosition_UsesInMemoryPositionAfterFirstWrite()
    {
        string appDataRoot = Path.Combine(Path.GetTempPath(), "PortfolioSaverTraceCacheTest", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", appDataRoot);
        try
        {
            string traceDirectory = Path.Combine(PathHelper.GetAppDataDirectory(), "Trace");
            string traceIndexPath = Path.Combine(traceDirectory, "trace.circular.idx");
            Directory.CreateDirectory(traceDirectory);

            FieldInfo positionField = typeof(TraceLog).GetField("_circularWritePosition", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("Could not find TraceLog._circularWritePosition.");
            MethodInfo writeCircularMethod = typeof(TraceLog).GetMethod("WriteCircular", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("Could not find TraceLog.WriteCircular.");

            string firstLine = $"{DateTimeOffset.UtcNow:O} | INFO | first-cache-line";
            string secondLine = $"{DateTimeOffset.UtcNow:O} | INFO | second-cache-line";
            int firstLength = Encoding.UTF8.GetByteCount(firstLine + Environment.NewLine);
            int secondLength = Encoding.UTF8.GetByteCount(secondLine + Environment.NewLine);

            positionField.SetValue(null, -1);
            writeCircularMethod.Invoke(null, [firstLine]);
            int firstPosition = int.Parse(File.ReadAllText(traceIndexPath));
            Assert.Equal(firstLength, firstPosition);

            File.WriteAllText(traceIndexPath, "0");
            writeCircularMethod.Invoke(null, [secondLine]);

            int secondPosition = int.Parse(File.ReadAllText(traceIndexPath));
            Assert.Equal(firstLength + secondLength, secondPosition);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", null);
            DeleteDirectoryWithRetry(appDataRoot);
        }
    }

    private static void DeleteDirectoryWithRetry(string path)
    {
        if (!Directory.Exists(path))
            return;

        IOException? lastIoException = null;
        for (int attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException ex)
            {
                lastIoException = ex;
                Thread.Sleep(50);
            }
        }

        if (lastIoException is not null)
            throw lastIoException;
    }

    private static async Task<bool> WaitForTraceAsync(
        string traceFilePath,
        string traceIndexPath,
        Func<string, bool> predicate)
    {
        for (int i = 0; i < 120; i++)
        {
            if (File.Exists(traceFilePath))
            {
                FileInfo info = new(traceFilePath);
                if (info.Length == 4 * 1024 * 1024)
                {
                    string text = ReadCircularText(traceFilePath, traceIndexPath);
                    if (predicate(text))
                        return true;
                }
            }

            await Task.Delay(100);
        }

        return false;
    }

    private static byte[] ReadAllBytesShared(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        byte[] bytes = new byte[stream.Length];
        _ = stream.Read(bytes, 0, bytes.Length);
        return bytes;
    }

    private static string ReadCircularText(string traceFilePath, string traceIndexPath)
    {
        byte[] bytes = ReadAllBytesShared(traceFilePath);
        if (!File.Exists(traceIndexPath))
            return Encoding.UTF8.GetString(bytes).Replace("\0", string.Empty);

        string rawIndex = Encoding.UTF8.GetString(ReadAllBytesShared(traceIndexPath)).Trim();
        if (!int.TryParse(rawIndex, out int position) || position <= 0 || position >= bytes.Length)
            return Encoding.UTF8.GetString(bytes).Replace("\0", string.Empty);

        byte[] reordered = new byte[bytes.Length];
        Buffer.BlockCopy(bytes, position, reordered, 0, bytes.Length - position);
        Buffer.BlockCopy(bytes, 0, reordered, bytes.Length - position, position);
        return Encoding.UTF8.GetString(reordered).Replace("\0", string.Empty);
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

        throw new DirectoryNotFoundException("Could not locate repo root from test AppContext.BaseDirectory.");
    }
}
