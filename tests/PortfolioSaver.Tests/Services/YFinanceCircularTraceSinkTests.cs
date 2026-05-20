using System.Text;
using YFinance.NET.Diagnostics;
using Xunit;

namespace PortfolioSaver.Tests.Services;

[Collection("EnvironmentSerial")]
public sealed class YFinanceCircularTraceSinkTests
{
    [Fact]
    public async Task YFinanceCircularTraceSink_WritesToDedicatedFourMegCircularFileUnderAppData()
    {
        string appDataRoot = Path.Combine(Path.GetTempPath(), "YFinanceTraceTest", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", appDataRoot);
        try
        {
            string traceDirectory = Path.Combine(appDataRoot, "Trace");
            string traceFilePath = Path.Combine(traceDirectory, "yfinance.circular.log");
            string traceIndexPath = Path.Combine(traceDirectory, "yfinance.circular.idx");
            Directory.CreateDirectory(traceDirectory);
            File.Delete(traceFilePath);
            File.Delete(traceIndexPath);
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

            Assert.True(observed, "Dedicated YFinance trace marker was not observed in the 4MB circular trace file.");
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
}
