using System.Text;
using System.Reflection;
using PortfolioSaver.Shared.Diagnostics;
using PortfolioSaver.Shared.Helpers;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class TraceLogTests
{
    [Fact]
    public async Task TraceLog_WritesToFourMegCircularFileUnderAppData()
    {
        string traceDirectory = Path.Combine(PathHelper.GetAppDataDirectory(), "Trace");
        string traceFilePath = Path.Combine(traceDirectory, "trace.circular.log");
        string traceIndexPath = Path.Combine(traceDirectory, "trace.circular.idx");
        string marker = "trace-test-" + Guid.NewGuid().ToString("N");
        MethodInfo? writeCircularMethod = typeof(TraceLog).GetMethod(
            "WriteCircular",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(writeCircularMethod);

        string line = $"{DateTimeOffset.UtcNow:O} | INFO | program=PortfolioSaver.Tests | source=TraceLogTests | function=TraceLog_WritesToFourMegCircularFileUnderAppData | {marker}";
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

    private static async Task<bool> WaitForTraceAsync(
        string traceFilePath,
        string traceIndexPath,
        Func<string, bool> predicate)
    {
        for (int i = 0; i < 200; i++)
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

            await Task.Delay(50);
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
            return Encoding.UTF8.GetString(bytes);

        string rawIndex = Encoding.UTF8.GetString(ReadAllBytesShared(traceIndexPath)).Trim();
        if (!int.TryParse(rawIndex, out int position) || position <= 0 || position >= bytes.Length)
            return Encoding.UTF8.GetString(bytes);

        byte[] reordered = new byte[bytes.Length];
        Buffer.BlockCopy(bytes, position, reordered, 0, bytes.Length - position);
        Buffer.BlockCopy(bytes, 0, reordered, bytes.Length - position, position);
        return Encoding.UTF8.GetString(reordered);
    }
}
