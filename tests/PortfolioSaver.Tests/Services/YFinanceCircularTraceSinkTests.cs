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
using System.Text;
using System.Reflection;
using YFinance.NET.Diagnostics;
using Xunit;

namespace PortfolioSaver.Tests.Services;

[Collection("EnvironmentSerial")]
public sealed class YFinanceCircularTraceSinkTests
{
    private const string TraceMaxMegabytesEnvironmentVariable = "DONOTPANICPORTFOLIOVISUALIZER_TRACE_MAX_MB";

    [Fact]
    public async Task YFinanceCircularTraceSink_WritesToDedicatedConfigurableCircularFileUnderAppData()
    {
        string appDataRoot = Path.Combine(Path.GetTempPath(), "YFinanceTraceTest", Guid.NewGuid().ToString("N"));
        string? previousProductRoot = Environment.GetEnvironmentVariable("DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT");
        string? previousLegacyLocalRoot = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT");
        string? previousLegacyAppDataRoot = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT");
        string? previousTraceMax = Environment.GetEnvironmentVariable(TraceMaxMegabytesEnvironmentVariable);
        Environment.SetEnvironmentVariable("DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT", appDataRoot);
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT", appDataRoot);
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", appDataRoot);
        Environment.SetEnvironmentVariable(TraceMaxMegabytesEnvironmentVariable, "4");
        try
        {
            YFinanceCircularTraceSink.ResetCircularStateForTests();
            string traceDirectory = Path.Combine(appDataRoot, "Trace");
            string traceFilePath = Path.Combine(traceDirectory, "yfinance.circular.log");
            string traceIndexPath = Path.Combine(traceDirectory, "yfinance.circular.idx");
            Directory.CreateDirectory(traceDirectory);
            string marker = "yfinance-trace-test-" + Guid.NewGuid().ToString("N");

            YFinanceCircularTraceSink.Instance.InfoState(
                "YFinanceCircularTraceSinkTests",
                "DedicatedTraceWrite",
                [new KeyValuePair<string, object?>("marker", marker)]);

            bool observed = await WaitForTraceAsync(
                traceFilePath,
                traceIndexPath,
                text =>
                {
                    if (!text.Contains(marker, StringComparison.Ordinal))
                        return false;

                    Assert.Contains("program=", text, StringComparison.Ordinal);
                    Assert.Contains("source=YFinanceCircularTraceSinkTests", text, StringComparison.Ordinal);
                    Assert.Contains("event=DedicatedTraceWrite", text, StringComparison.Ordinal);
                    return true;
                });

            Assert.True(observed, "Dedicated YFinance trace marker was not observed in the circular trace file.");
            Assert.Equal(4 * 1024 * 1024, new FileInfo(traceFilePath).Length);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT", previousProductRoot);
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT", previousLegacyLocalRoot);
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", previousLegacyAppDataRoot);
            Environment.SetEnvironmentVariable(TraceMaxMegabytesEnvironmentVariable, previousTraceMax);
        }
    }

    [Fact]
    public void YFinanceCircularTraceSink_RedactsSecretLikeStructuredFields()
    {
        MethodInfo formatter = typeof(YFinanceCircularTraceSink).GetMethod(
            "BuildStructuredMessage",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find YFinanceCircularTraceSink.BuildStructuredMessage.");

        string message = (string)(formatter.Invoke(
            null,
            [
                "SecretTrace",
                new[]
                {
                    new KeyValuePair<string, object?>("launch_token", "owned-server-token"),
                    new KeyValuePair<string, object?>("Authorization", "Bearer abc123"),
                    new KeyValuePair<string, object?>("message", "api_key=abc123,def credential:letmein")
                }
            ]) ?? throw new InvalidOperationException("Structured message formatter returned null."));

        Assert.Contains("launch_token=<redacted>", message, StringComparison.Ordinal);
        Assert.Contains("Authorization=<redacted>", message, StringComparison.Ordinal);
        Assert.Contains("api_key=<redacted>", message, StringComparison.Ordinal);
        Assert.Contains("credential:<redacted>", message, StringComparison.Ordinal);
        Assert.DoesNotContain("owned-server-token", message, StringComparison.Ordinal);
        Assert.DoesNotContain("abc123", message, StringComparison.Ordinal);
        Assert.DoesNotContain("def", message, StringComparison.Ordinal);
        Assert.DoesNotContain("letmein", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task YFinanceCircularTraceSink_ConcurrentInstanceAccessAndWritesAreStable()
    {
        string appDataRoot = Path.Combine(Path.GetTempPath(), "YFinanceTraceStressTest", Guid.NewGuid().ToString("N"));
        string? previousProductRoot = Environment.GetEnvironmentVariable("DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT");
        string? previousLegacyLocalRoot = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT");
        string? previousLegacyAppDataRoot = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT");
        Environment.SetEnvironmentVariable("DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT", appDataRoot);
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT", appDataRoot);
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", appDataRoot);
        try
        {
            string traceDirectory = Path.Combine(appDataRoot, "Trace");
            string traceFilePath = Path.Combine(traceDirectory, "yfinance.circular.log");
            string traceIndexPath = Path.Combine(traceDirectory, "yfinance.circular.idx");
            string markerPrefix = "yfinance-trace-stress-" + Guid.NewGuid().ToString("N");

            int writeCount = 100;
            YFinanceCircularTraceSink[] instances = await Task.WhenAll(Enumerable.Range(0, writeCount).Select(index => Task.Run(() =>
            {
                string marker = $"{markerPrefix}-{index:D3}";
                YFinanceCircularTraceSink sink = YFinanceCircularTraceSink.Instance;
                sink.InfoState(
                    "YFinanceCircularTraceSinkStress",
                    "ConcurrentTraceWrite",
                    [
                        new KeyValuePair<string, object?>("marker", marker),
                        new KeyValuePair<string, object?>("index", index)
                    ]);
                return sink;
            })));

            YFinanceCircularTraceSink first = instances[0];
            Assert.All(instances, instance => Assert.Same(first, instance));

            bool observed = await WaitForTraceAsync(
                traceFilePath,
                traceIndexPath,
                text =>
                {
                    for (int index = 0; index < writeCount; index++)
                    {
                        if (!text.Contains($"{markerPrefix}-{index:D3}", StringComparison.Ordinal))
                            return false;
                    }

                    Assert.DoesNotContain("\0", text, StringComparison.Ordinal);
                    Assert.Contains("source=YFinanceCircularTraceSinkStress", text, StringComparison.Ordinal);
                    return true;
                });

            Assert.True(observed, "Concurrent YFinance trace marker was not observed in the circular trace file.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT", previousProductRoot);
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT", previousLegacyLocalRoot);
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", previousLegacyAppDataRoot);
        }
    }

    [Fact]
    public void YFinanceCircularTraceSink_AvoidsPerLineDiskSyncAndCachesCircularCursor()
    {
        string repoRoot = GetRepoRoot();
        string source = File.ReadAllText(Path.Combine(
            repoRoot,
            "YFinance.net",
            "YFinance.NET",
            "Diagnostics",
            "YFinanceCircularTraceSink.cs"));

        Assert.Contains("private static int _circularWritePosition = -1;", source, StringComparison.Ordinal);
        Assert.Contains("await Task.Delay(250).ConfigureAwait(false);", source, StringComparison.Ordinal);
        Assert.Contains("private const int MaxTraceBatchLines = 512;", source, StringComparison.Ordinal);
        Assert.Contains("private const int TraceIndexCheckpointLines = 64;", source, StringComparison.Ordinal);
        Assert.Contains("while (lines.Count < MaxTraceBatchLines && Queue.TryDequeue(out string? nextLine))", source, StringComparison.Ordinal);
        Assert.Contains("WriteCircularBatch(lines);", source, StringComparison.Ordinal);
        Assert.Contains("_circularWritePosition = nextPosition;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("stream.Flush(true)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void YFinanceCircularTraceSink_WriteCircularBatchPreservesOrderAndFinalCursor()
    {
        string appDataRoot = Path.Combine(Path.GetTempPath(), "YFinanceTraceBatchTest", Guid.NewGuid().ToString("N"));
        string? previousProductRoot = Environment.GetEnvironmentVariable("DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT");
        string? previousLegacyLocalRoot = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT");
        string? previousLegacyAppDataRoot = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT");
        string? previousTraceMax = Environment.GetEnvironmentVariable(TraceMaxMegabytesEnvironmentVariable);
        Environment.SetEnvironmentVariable("DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT", appDataRoot);
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT", appDataRoot);
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", appDataRoot);
        Environment.SetEnvironmentVariable(TraceMaxMegabytesEnvironmentVariable, "4");
        try
        {
            YFinanceCircularTraceSink.ResetCircularStateForTests();
            string traceDirectory = Path.Combine(appDataRoot, "Trace");
            string traceFilePath = Path.Combine(traceDirectory, "yfinance.circular.log");
            string traceIndexPath = Path.Combine(traceDirectory, "yfinance.circular.idx");
            string marker = "yfinance-batch-" + Guid.NewGuid().ToString("N");
            string[] lines =
            [
                $"{DateTimeOffset.UtcNow:O} | INFO | {marker}-001",
                $"{DateTimeOffset.UtcNow:O} | INFO | {marker}-002",
                $"{DateTimeOffset.UtcNow:O} | INFO | {marker}-003"
            ];

            MethodInfo writeBatchMethod = typeof(YFinanceCircularTraceSink).GetMethod("WriteCircularBatch", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("Could not find YFinanceCircularTraceSink.WriteCircularBatch.");
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
        }
    }

    [Fact]
    public void YFinanceCircularTraceSink_CorruptCircularIndexRecoversWithoutThrowing()
    {
        string appDataRoot = Path.Combine(Path.GetTempPath(), "YFinanceTraceCorruptIndexTest", Guid.NewGuid().ToString("N"));
        string? previousProductRoot = Environment.GetEnvironmentVariable("DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT");
        string? previousLegacyLocalRoot = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT");
        string? previousLegacyAppDataRoot = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT");
        string? previousTraceMax = Environment.GetEnvironmentVariable(TraceMaxMegabytesEnvironmentVariable);
        Environment.SetEnvironmentVariable("DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT", appDataRoot);
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT", appDataRoot);
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", appDataRoot);
        Environment.SetEnvironmentVariable(TraceMaxMegabytesEnvironmentVariable, "4");
        try
        {
            YFinanceCircularTraceSink.ResetCircularStateForTests();
            string traceDirectory = Path.Combine(appDataRoot, "Trace");
            string traceFilePath = Path.Combine(traceDirectory, "yfinance.circular.log");
            string traceIndexPath = Path.Combine(traceDirectory, "yfinance.circular.idx");
            Directory.CreateDirectory(traceDirectory);
            File.WriteAllText(traceIndexPath, "not-a-number");

            MethodInfo writeCircularMethod = typeof(YFinanceCircularTraceSink).GetMethod("WriteCircular", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("Could not find YFinanceCircularTraceSink.WriteCircular.");
            FieldInfo fileSyncField = typeof(YFinanceCircularTraceSink).GetField("FileSync", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("Could not find YFinanceCircularTraceSink.FileSync.");
            object fileSync = fileSyncField.GetValue(null)
                ?? throw new InvalidOperationException("YFinanceCircularTraceSink.FileSync was null.");
            string marker = "yfinance-corrupt-index-" + Guid.NewGuid().ToString("N");

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
    public async Task YFinanceCircularTraceSink_BackgroundWorkerDrainsBurstWithoutLosingLines()
    {
        string appDataRoot = Path.Combine(Path.GetTempPath(), "YFinanceTraceBurstTest", Guid.NewGuid().ToString("N"));
        string? previousProductRoot = Environment.GetEnvironmentVariable("DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT");
        string? previousLegacyLocalRoot = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT");
        string? previousLegacyAppDataRoot = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT");
        string? previousTraceMax = Environment.GetEnvironmentVariable(TraceMaxMegabytesEnvironmentVariable);
        Environment.SetEnvironmentVariable("DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT", appDataRoot);
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT", appDataRoot);
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", appDataRoot);
        Environment.SetEnvironmentVariable(TraceMaxMegabytesEnvironmentVariable, "4");
        try
        {
            YFinanceCircularTraceSink.ResetCircularStateForTests();
            string traceDirectory = Path.Combine(appDataRoot, "Trace");
            string traceFilePath = Path.Combine(traceDirectory, "yfinance.circular.log");
            string traceIndexPath = Path.Combine(traceDirectory, "yfinance.circular.idx");
            string markerPrefix = "yfinance-worker-burst-" + Guid.NewGuid().ToString("N");
            const int writeCount = 130;

            for (int index = 0; index < writeCount; index++)
            {
                YFinanceCircularTraceSink.Instance.InfoState(
                    "YFinanceCircularTraceSinkBurst",
                    "BurstTraceWrite",
                    [new KeyValuePair<string, object?>("marker", $"{markerPrefix}-{index:D3}")]);
            }

            bool observed = await WaitForTraceAsync(
                traceFilePath,
                traceIndexPath,
                text => Enumerable.Range(0, writeCount)
                    .All(index => text.Contains($"{markerPrefix}-{index:D3}", StringComparison.Ordinal)));

            Assert.True(observed, "YFinance trace background worker did not persist every burst marker.");
            Assert.True(int.Parse(File.ReadAllText(traceIndexPath)) > 0);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT", previousProductRoot);
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT", previousLegacyLocalRoot);
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", previousLegacyAppDataRoot);
            Environment.SetEnvironmentVariable(TraceMaxMegabytesEnvironmentVariable, previousTraceMax);
        }
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
                string text = ReadCircularText(traceFilePath, traceIndexPath);
                if (predicate(text))
                    return true;
            }

            await Task.Delay(100);
        }

        return false;
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
        foreach (string startDirectory in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            DirectoryInfo? directory = new(startDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "PortfolioScreensaver.sln")))
                    return directory.FullName;

                directory = directory.Parent;
            }
        }

        throw new InvalidOperationException("Could not find repository root from test working directories.");
    }
}
