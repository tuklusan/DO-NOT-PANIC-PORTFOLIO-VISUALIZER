using PortfolioSaver.Data.Services;
using PortfolioSaver.Core.Models;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class HistoricalCacheServiceTests
{
    private static string CreateTempRoot()
    {
        return Path.Combine(Path.GetTempPath(), "PortfolioSaver.Tests", Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public async Task LoadAsync_EmptyCacheFileReturnsNullAndDeletesFile()
    {
        string root = CreateTempRoot();
        try
        {
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "VOO.json");
            await File.WriteAllTextAsync(path, string.Empty);

            HistoricalCacheService service = new(root);
            TickerHistorySnapshot? snapshot = await service.LoadAsync("VOO");

            Assert.Null(snapshot);
            Assert.False(File.Exists(path));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_CorruptCacheFileReturnsNullAndDeletesFile()
    {
        string root = CreateTempRoot();
        try
        {
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "VOO.json");
            await File.WriteAllTextAsync(path, "{not-json");

            HistoricalCacheService service = new(root);
            TickerHistorySnapshot? snapshot = await service.LoadAsync("VOO");

            Assert.Null(snapshot);
            Assert.False(File.Exists(path));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_LockedCacheFileReturnsNullAndLeavesFile()
    {
        string root = CreateTempRoot();
        try
        {
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "VOO.json");
            await File.WriteAllTextAsync(path, "{\"symbol\":\"VOO\"}");

            await using FileStream _ = new(
                path,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None);

            HistoricalCacheService service = new(root);
            TickerHistorySnapshot? snapshot = await service.LoadAsync("VOO");

            Assert.Null(snapshot);
            Assert.True(File.Exists(path));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SaveThenLoadAsync_RoundTripsFreshSnapshot()
    {
        string root = CreateTempRoot();
        try
        {
            HistoricalCacheService service = new(root);
            TickerHistorySnapshot expected = new()
            {
                Symbol = "VOO",
                LookbackDays = 7,
                FetchTimestampUtc = DateTimeOffset.UtcNow,
                Points =
                [
                    new HistoricalPricePoint { TimestampUtc = DateTimeOffset.UtcNow.AddMinutes(-1), Close = 123.45m }
                ]
            };

            await service.SaveAsync(expected);
            Assert.True(File.Exists(Path.Combine(root, "VOO.json")));

            TickerHistorySnapshot? actual = await service.LoadAsync("VOO");

            Assert.NotNull(actual);
            Assert.Equal("VOO", actual.Symbol);
            Assert.Equal(7, actual.LookbackDays);
            HistoricalPricePoint point = Assert.Single(actual.Points);
            Assert.Equal(123.45m, point.Close);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PurgeExpiredAsync_DeletesExpiredJsonFilesOffCallingThread()
    {
        string root = CreateTempRoot();
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
            int callerThreadId = 0;
            int purgeThreadId = 0;
            service.PurgeStartedForTesting = () => purgeThreadId = Environment.CurrentManagedThreadId;

            Task<Task> caller = Task.Factory.StartNew(
                () =>
                {
                    callerThreadId = Environment.CurrentManagedThreadId;
                    return service.PurgeExpiredAsync();
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);

            Task purge = await caller.WaitAsync(TimeSpan.FromSeconds(5));
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
        string root = CreateTempRoot();
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
        string root = CreateTempRoot();
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
