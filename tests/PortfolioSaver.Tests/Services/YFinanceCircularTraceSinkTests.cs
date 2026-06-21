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
        string? previousTraceMax = Environment.GetEnvironmentVariable(TraceMaxMegabytesEnvironmentVariable);
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
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", null);
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
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", null);
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
}
