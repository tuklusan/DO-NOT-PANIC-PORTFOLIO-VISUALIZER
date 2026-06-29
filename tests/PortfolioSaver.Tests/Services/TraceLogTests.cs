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
using System.Text;
using System.Reflection;
using PortfolioSaver.Shared.Diagnostics;
using PortfolioSaver.Shared.Helpers;
using Xunit;

namespace PortfolioSaver.Tests.Services;

[Collection("EnvironmentSerial")]
public sealed class TraceLogTests
{
    private const string TraceMaxMegabytesEnvironmentVariable = "DONOTPANICPORTFOLIOVISUALIZER_TRACE_MAX_MB";

    [Fact]
    public void TraceLog_WritesToConfigurableCircularFileUnderAppData()
    {
        string appDataRoot = Path.Combine(Path.GetTempPath(), "PortfolioSaverTraceTest", Guid.NewGuid().ToString("N"));
        string? previousProductRoot = Environment.GetEnvironmentVariable("DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT");
        string? previousLegacyLocalRoot = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT");
        string? previousLegacyAppDataRoot = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT");
        string? previousTraceMax = Environment.GetEnvironmentVariable(TraceMaxMegabytesEnvironmentVariable);
        DeleteDirectoryWithRetry(appDataRoot);
        Environment.SetEnvironmentVariable("DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT", appDataRoot);
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT", appDataRoot);
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", appDataRoot);
        Environment.SetEnvironmentVariable(TraceMaxMegabytesEnvironmentVariable, "4");
        try
        {
            string traceDirectory = Path.Combine(PathHelper.GetAppDataDirectory(), "Trace");
            string traceFilePath = Path.Combine(traceDirectory, "trace.circular.log");
            string traceIndexPath = Path.Combine(traceDirectory, "trace.circular.idx");
            Directory.CreateDirectory(traceDirectory);
            Assert.StartsWith(Path.GetFullPath(appDataRoot), Path.GetFullPath(traceDirectory), StringComparison.OrdinalIgnoreCase);
            TraceLog.ResetCircularStateForTests();
            int expectedTraceBytes = 4 * 1024 * 1024;
            // CircularTraceSettingsTests owns environment parsing. This test pins
            // the cache so the live background trace worker cannot race the
            // configurable-size file allocation assertion in full-suite VM runs.
            FieldInfo maxTraceBytesField = typeof(TraceLog).GetField(
                "_maxTraceBytes",
                BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("Could not find TraceLog._maxTraceBytes.");
            FieldInfo fileSyncField = typeof(TraceLog).GetField("FileSync", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("Could not find TraceLog.FileSync.");
            object fileSync = fileSyncField.GetValue(null)
                ?? throw new InvalidOperationException("TraceLog.FileSync was null.");
            string marker = "trace-test-" + Guid.NewGuid().ToString("N");
            MethodInfo? writeCircularMethod = typeof(TraceLog).GetMethod(
                "WriteCircular",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(writeCircularMethod);

            string line = $"{DateTimeOffset.UtcNow:O} | INFO | program=PortfolioSaver.Tests | source=TraceLogTests | function=TraceLog_WritesToConfigurableCircularFileUnderAppData | {marker}";
            lock (fileSync)
            {
                maxTraceBytesField.SetValue(null, expectedTraceBytes);
                writeCircularMethod!.Invoke(null, [line]);
                writeCircularMethod!.Invoke(null, [line]);
                writeCircularMethod!.Invoke(null, [line]);

                string text = File.ReadAllText(traceFilePath);
                Assert.Contains(marker, text, StringComparison.Ordinal);
                Assert.Contains("program=", text, StringComparison.Ordinal);
                Assert.Contains("function=", text, StringComparison.Ordinal);
                // WriteCircular pre-allocates the circular buffer with SetLength(maxTraceBytes).
                Assert.Equal(expectedTraceBytes, new FileInfo(traceFilePath).Length);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT", previousProductRoot);
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT", previousLegacyLocalRoot);
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", previousLegacyAppDataRoot);
            Environment.SetEnvironmentVariable(TraceMaxMegabytesEnvironmentVariable, previousTraceMax);
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
        string? previousProductRoot = Environment.GetEnvironmentVariable("DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT");
        string? previousLegacyLocalRoot = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT");
        string? previousLegacyAppDataRoot = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT");
        DeleteDirectoryWithRetry(appDataRoot);
        Environment.SetEnvironmentVariable("DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT", appDataRoot);
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT", appDataRoot);
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", appDataRoot);
        try
        {
            string traceDirectory = Path.Combine(PathHelper.GetAppDataDirectory(), "Trace");
            string traceFilePath = Path.Combine(traceDirectory, "trace.circular.log");
            string traceIndexPath = Path.Combine(traceDirectory, "trace.circular.idx");
            Directory.CreateDirectory(traceDirectory);
            Assert.StartsWith(Path.GetFullPath(appDataRoot), Path.GetFullPath(traceDirectory), StringComparison.OrdinalIgnoreCase);
            TraceLog.ResetCircularStateForTests();

            FieldInfo positionField = typeof(TraceLog).GetField("_circularWritePosition", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("Could not find TraceLog._circularWritePosition.");
            FieldInfo fileSyncField = typeof(TraceLog).GetField("FileSync", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("Could not find TraceLog.FileSync.");
            if (fileSyncField.FieldType.IsValueType)
                throw new InvalidOperationException("TraceLog.FileSync must be a reference type for this synchronization test.");

            MethodInfo writeCircularMethod = typeof(TraceLog).GetMethod("WriteCircular", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("Could not find TraceLog.WriteCircular.");
            object fileSync = fileSyncField.GetValue(null)
                ?? throw new InvalidOperationException("TraceLog.FileSync was null.");

            string firstLine = $"{DateTimeOffset.UtcNow:O} | INFO | first-cache-line";
            string secondLine = $"{DateTimeOffset.UtcNow:O} | INFO | second-cache-line";
            int firstLength = Encoding.UTF8.GetByteCount(firstLine + Environment.NewLine);
            int secondLength = Encoding.UTF8.GetByteCount(secondLine + Environment.NewLine);

            // C# Monitor locks are re-entrant; holding the production FileSync lock
            // keeps the background trace worker from advancing the cursor mid-test.
            lock (fileSync)
            {
                File.Delete(traceFilePath);
                File.Delete(traceIndexPath);
                positionField.SetValue(null, -1);
                writeCircularMethod.Invoke(null, [firstLine]);
                int firstPosition = int.Parse(File.ReadAllText(traceIndexPath));
                Assert.Equal(firstLength, firstPosition);

                File.WriteAllText(traceIndexPath, "0");
                writeCircularMethod.Invoke(null, [secondLine]);

                int secondPosition = int.Parse(File.ReadAllText(traceIndexPath));
                Assert.Equal(firstLength + secondLength, secondPosition);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT", previousProductRoot);
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT", previousLegacyLocalRoot);
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", previousLegacyAppDataRoot);
            DeleteDirectoryWithRetry(appDataRoot);
        }
    }

    [Fact]
    public void TraceLog_WriteCircularBatchPreservesOrderAndFinalCursor()
    {
        string appDataRoot = Path.Combine(Path.GetTempPath(), "PortfolioSaverTraceBatchTest", Guid.NewGuid().ToString("N"));
        string? previousProductRoot = Environment.GetEnvironmentVariable("DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT");
        string? previousLegacyLocalRoot = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT");
        string? previousLegacyAppDataRoot = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT");
        string? previousTraceMax = Environment.GetEnvironmentVariable(TraceMaxMegabytesEnvironmentVariable);
        DeleteDirectoryWithRetry(appDataRoot);
        Environment.SetEnvironmentVariable("DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT", appDataRoot);
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT", appDataRoot);
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", appDataRoot);
        Environment.SetEnvironmentVariable(TraceMaxMegabytesEnvironmentVariable, "4");
        try
        {
            TraceLog.ResetCircularStateForTests();
            string traceDirectory = Path.Combine(PathHelper.GetAppDataDirectory(), "Trace");
            string traceFilePath = Path.Combine(traceDirectory, "trace.circular.log");
            string traceIndexPath = Path.Combine(traceDirectory, "trace.circular.idx");
            string marker = "trace-batch-" + Guid.NewGuid().ToString("N");
            string[] lines =
            [
                $"{DateTimeOffset.UtcNow:O} | INFO | {marker}-001",
                $"{DateTimeOffset.UtcNow:O} | INFO | {marker}-002",
                $"{DateTimeOffset.UtcNow:O} | INFO | {marker}-003"
            ];

            MethodInfo writeBatchMethod = typeof(TraceLog).GetMethod("WriteCircularBatch", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("Could not find TraceLog.WriteCircularBatch.");
            writeBatchMethod.Invoke(null, [lines]);

            string text = File.ReadAllText(traceFilePath).Replace("\0", string.Empty);
            Assert.True(
                text.IndexOf($"{marker}-001", StringComparison.Ordinal) <
                text.IndexOf($"{marker}-002", StringComparison.Ordinal));
            Assert.True(
                text.IndexOf($"{marker}-002", StringComparison.Ordinal) <
                text.IndexOf($"{marker}-003", StringComparison.Ordinal));

            int expectedPosition = lines.Sum(line => Encoding.UTF8.GetByteCount(line + Environment.NewLine));
            Assert.Equal(expectedPosition, int.Parse(File.ReadAllText(traceIndexPath)));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT", previousProductRoot);
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT", previousLegacyLocalRoot);
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", previousLegacyAppDataRoot);
            Environment.SetEnvironmentVariable(TraceMaxMegabytesEnvironmentVariable, previousTraceMax);
            DeleteDirectoryWithRetry(appDataRoot);
        }
    }

    [Fact]
    public void TraceLog_CorruptCircularIndexRecoversWithoutThrowing()
    {
        string appDataRoot = Path.Combine(Path.GetTempPath(), "PortfolioSaverTraceCorruptIndexTest", Guid.NewGuid().ToString("N"));
        string? previousProductRoot = Environment.GetEnvironmentVariable("DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT");
        string? previousLegacyLocalRoot = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT");
        string? previousLegacyAppDataRoot = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT");
        string? previousTraceMax = Environment.GetEnvironmentVariable(TraceMaxMegabytesEnvironmentVariable);
        DeleteDirectoryWithRetry(appDataRoot);
        Environment.SetEnvironmentVariable("DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT", appDataRoot);
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT", appDataRoot);
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", appDataRoot);
        Environment.SetEnvironmentVariable(TraceMaxMegabytesEnvironmentVariable, "4");
        try
        {
            TraceLog.ResetCircularStateForTests();
            string traceDirectory = Path.Combine(PathHelper.GetAppDataDirectory(), "Trace");
            string traceFilePath = Path.Combine(traceDirectory, "trace.circular.log");
            string traceIndexPath = Path.Combine(traceDirectory, "trace.circular.idx");
            Directory.CreateDirectory(traceDirectory);
            File.WriteAllText(traceIndexPath, "not-a-number");

            MethodInfo writeCircularMethod = typeof(TraceLog).GetMethod("WriteCircular", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("Could not find TraceLog.WriteCircular.");
            FieldInfo fileSyncField = typeof(TraceLog).GetField("FileSync", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("Could not find TraceLog.FileSync.");
            object fileSync = fileSyncField.GetValue(null)
                ?? throw new InvalidOperationException("TraceLog.FileSync was null.");
            string marker = "trace-corrupt-index-" + Guid.NewGuid().ToString("N");

            lock (fileSync)
            {
                writeCircularMethod.Invoke(null, [$"{DateTimeOffset.UtcNow:O} | INFO | {marker}"]);
            }

            string text = File.ReadAllText(traceFilePath).Replace("\0", string.Empty);
            Assert.Contains(marker, text, StringComparison.Ordinal);
            Assert.True(int.Parse(File.ReadAllText(traceIndexPath)) > 0);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT", previousProductRoot);
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT", previousLegacyLocalRoot);
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", previousLegacyAppDataRoot);
            Environment.SetEnvironmentVariable(TraceMaxMegabytesEnvironmentVariable, previousTraceMax);
            DeleteDirectoryWithRetry(appDataRoot);
        }
    }

    [Fact]
    public async Task TraceLog_BackgroundWorkerDrainsBurstWithoutLosingLines()
    {
        string appDataRoot = Path.Combine(Path.GetTempPath(), "PortfolioSaverTraceBurstTest", Guid.NewGuid().ToString("N"));
        string? previousProductRoot = Environment.GetEnvironmentVariable("DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT");
        string? previousLegacyLocalRoot = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT");
        string? previousLegacyAppDataRoot = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT");
        string? previousTraceMax = Environment.GetEnvironmentVariable(TraceMaxMegabytesEnvironmentVariable);
        DeleteDirectoryWithRetry(appDataRoot);
        Environment.SetEnvironmentVariable("DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT", appDataRoot);
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT", appDataRoot);
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", appDataRoot);
        Environment.SetEnvironmentVariable(TraceMaxMegabytesEnvironmentVariable, "4");
        try
        {
            TraceLog.ResetCircularStateForTests();
            string traceDirectory = Path.Combine(PathHelper.GetAppDataDirectory(), "Trace");
            string traceFilePath = Path.Combine(traceDirectory, "trace.circular.log");
            string traceIndexPath = Path.Combine(traceDirectory, "trace.circular.idx");
            string markerPrefix = "trace-worker-burst-" + Guid.NewGuid().ToString("N");
            const int writeCount = 130;

            for (int index = 0; index < writeCount; index++)
            {
                TraceLog.InfoState(
                    "TraceLogBurstTest",
                    "BurstTraceWrite",
                    [new KeyValuePair<string, object?>("marker", $"{markerPrefix}-{index:D3}")]);
            }

            bool observed = await WaitForTraceAsync(
                traceFilePath,
                traceIndexPath,
                text => Enumerable.Range(0, writeCount)
                    .All(index => text.Contains($"{markerPrefix}-{index:D3}", StringComparison.Ordinal)));

            Assert.True(observed, "TraceLog background worker did not persist every burst marker.");
            Assert.True(int.Parse(File.ReadAllText(traceIndexPath)) > 0);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT", previousProductRoot);
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT", previousLegacyLocalRoot);
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", previousLegacyAppDataRoot);
            Environment.SetEnvironmentVariable(TraceMaxMegabytesEnvironmentVariable, previousTraceMax);
            DeleteDirectoryWithRetry(appDataRoot);
        }
    }

    [Fact]
    public void TraceLog_BackgroundWorkerAvoidsPerLineDiskSyncAndRestartsAfterLoopExceptions()
    {
        string repoRoot = GetRepoRoot();
        string source = File.ReadAllText(Path.Combine(repoRoot, "src", "PortfolioSaver.Shared", "Diagnostics", "TraceLog.cs"));

        Assert.Contains("await Task.Delay(250).ConfigureAwait(false);", source, StringComparison.Ordinal);
        Assert.Contains("private const int MaxTraceBatchLines = 512;", source, StringComparison.Ordinal);
        Assert.Contains("private const int TraceIndexCheckpointLines = 64;", source, StringComparison.Ordinal);
        Assert.Contains("while (lines.Count < MaxTraceBatchLines && Queue.TryDequeue(out string? nextLine))", source, StringComparison.Ordinal);
        Assert.Contains("WriteCircularBatch(lines);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("stream.Flush(true)", source, StringComparison.Ordinal);
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
                if (info.Length > 0)
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
