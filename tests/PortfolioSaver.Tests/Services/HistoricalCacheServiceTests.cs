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
            using ManualResetEventSlim purgeStarted = new();
            using ManualResetEventSlim releasePurge = new();
            int callerThreadId = 0;
            int purgeThreadId = 0;
            service.PurgeStartedForTesting = () =>
            {
                purgeThreadId = Environment.CurrentManagedThreadId;
                purgeStarted.Set();
                Assert.True(releasePurge.Wait(TimeSpan.FromSeconds(5)));
            };

            Task<Task> caller = Task.Factory.StartNew(
                () =>
                {
                    callerThreadId = Environment.CurrentManagedThreadId;
                    Task purgeTask = service.PurgeExpiredAsync();
                    Assert.True(purgeStarted.Wait(TimeSpan.FromSeconds(5)));
                    return purgeTask;
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);

            Task purge = await caller.WaitAsync(TimeSpan.FromSeconds(5));
            releasePurge.Set();
            await purge.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.NotEqual(callerThreadId, purgeThreadId);
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
    public async Task PurgeExpiredAsync_CancellationAfterWorkerStartsStopsPromptly()
    {
        string root = Path.Combine(Path.GetTempPath(), "PortfolioSaver.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            for (int i = 0; i < 25; i++)
                await File.WriteAllTextAsync(Path.Combine(root, $"CACHE-{i:000}.json"), "{}");

            HistoricalCacheService service = new(root);
            using CancellationTokenSource cts = new();
            int iterationCount = 0;
            service.PurgeIterationForTesting = () =>
            {
                if (Interlocked.Increment(ref iterationCount) == 1)
                    cts.Cancel();
            };

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.PurgeExpiredAsync(cts.Token).WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.True(iterationCount > 0);
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
