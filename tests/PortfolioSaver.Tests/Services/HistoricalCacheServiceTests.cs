using PortfolioSaver.Data.Services;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class HistoricalCacheServiceTests
{
    [Fact]
    public async Task PurgeExpiredAsync_DeletesExpiredJsonFilesOffCallingThread()
    {
        string root = Path.Combine(Path.GetTempPath(), "PortfolioSaver.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            string expiredPath = Path.Combine(root, "EXPIRED.json");
            string freshPath = Path.Combine(root, "FRESH.json");
            await File.WriteAllTextAsync(expiredPath, "{}");
            await File.WriteAllTextAsync(freshPath, "{}");
            File.SetLastWriteTimeUtc(expiredPath, DateTime.UtcNow.AddDays(-30));
            File.SetLastWriteTimeUtc(freshPath, DateTime.UtcNow);

            HistoricalCacheService service = new(root);

            Task purge = service.PurgeExpiredAsync();

            await purge.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(File.Exists(expiredPath));
            Assert.True(File.Exists(freshPath));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PurgeExpiredAsync_PreCanceledTokenStopsPromptly()
    {
        string root = Path.Combine(Path.GetTempPath(), "PortfolioSaver.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            HistoricalCacheService service = new(root);
            using CancellationTokenSource cts = new();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.PurgeExpiredAsync(cts.Token));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
