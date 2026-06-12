using System.Collections.Concurrent;
using System.Reflection;
using System.Net.Http;
using System.Text.Json;
using PortfolioSaver.Core.Enums;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Data.Interfaces;
using PortfolioSaver.Data.Providers;
using PortfolioSaver.Data.Services;
using PortfolioSaver.Render.Services;
using PortfolioSaver.Media.Services;
using PortfolioSaver.Shared.Services;
using PortfolioSaver.Screensaver.Services;
using PortfolioSaver.Shared.Helpers;
using Xunit;
using YFinance.NET.Client;
using YFinance.NET.Config;
using YFinance.NET.Features.Quotes;
using YFinance.NET.Protocol.Dtos;
using YFinance.NET.Transport;
using YQuoteSnapshot = YFinance.NET.Models.QuoteSnapshot;
using YTickerInfo = YFinance.NET.Models.TickerInfo;

namespace PortfolioSaver.Tests.Services;

[Collection("EnvironmentSerial")]
public sealed class Nb040BehaviorTests
{
    [Fact]
    public void YFinanceOptions_DefaultToTenMinuteCachesAndGenericUserAgent()
    {
        string? previousProductOverride = Environment.GetEnvironmentVariable("DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT");
        string? previousLocalOverride = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT");
        string? previousLegacyOverride = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT");
        Environment.SetEnvironmentVariable("DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT", null);
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT", null);
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", null);

        try
        {
            YFinanceOptions options = new();

            Assert.Equal(TimeSpan.FromMinutes(10), options.DefaultCacheTtl);
            Assert.Equal(TimeSpan.FromMinutes(10), options.SummaryCacheTtl);
            Assert.Equal(TimeSpan.FromMinutes(10), options.PersistentMetadataCacheTtl);
            Assert.Equal(
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    PathHelper.AppLocalDataFolderName,
                    "Caches",
                    "YFinance"),
                options.PersistentCacheRootPath);
            Assert.DoesNotContain("PortfolioSaver", options.UserAgent, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Don't Panic", options.UserAgent, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Visualiz", options.UserAgent, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT", previousProductOverride);
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT", previousLocalOverride);
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", previousLegacyOverride);
        }
    }

    [Fact]
    public void YFinanceOptions_PersistentCacheRootHonorsProductLocalDataOverride()
    {
        string? previousProductOverride = Environment.GetEnvironmentVariable("DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT");
        string? previousLocalOverride = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT");
        string? previousLegacyOverride = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT");
        string overrideRoot = Path.Combine(Path.GetTempPath(), "PortfolioSaverTests", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT", overrideRoot);
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT", null);
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", null);

        try
        {
            YFinanceOptions options = new();

            Assert.Equal(Path.Combine(Path.GetFullPath(overrideRoot), "Caches", "YFinance"), options.PersistentCacheRootPath);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT", previousProductOverride);
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT", previousLocalOverride);
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", previousLegacyOverride);
            if (Directory.Exists(overrideRoot))
                Directory.Delete(overrideRoot, recursive: true);
        }
    }

    [Fact]
    public void YFinanceRequestIdentifiers_DoNotContainApplicationBranding()
    {
        using YahooFinanceHttpClient client = new(new YFinanceOptions());
        MethodInfo buildRequest = typeof(YahooFinanceHttpClient).GetMethod(
            "BuildRequest",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("YahooFinanceHttpClient.BuildRequest not found.");

        Uri requestUri = new("https://query1.finance.yahoo.com/v7/finance/quote?symbols=AAPL");
        YahooSessionState session = new("crumb", "cookie=value", DateTimeOffset.UtcNow.AddMinutes(30));
        using var request = Assert.IsType<System.Net.Http.HttpRequestMessage>(buildRequest.Invoke(client, [requestUri, session]));

        Assert.True(request.Headers.TryGetValues("x-yahoo-request-id", out IEnumerable<string>? values));
        string requestId = Assert.Single(values);
        Assert.DoesNotContain("PortfolioSaver", requestId, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Don't Panic", requestId, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Visualiz", requestId, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task YFinanceRuntimeClientFactory_AllowsConcurrentClientWork()
    {
        // This test verifies factory scheduling only; the client argument is intentionally unused.
        using IDisposable serverBypass = YFinanceRuntimeClientFactory.SuppressServerStartupForTests();
        int concurrent = 0;
        int maxConcurrent = 0;

        Task<int> first = YFinanceRuntimeClientFactory.RunSerializedAsync(
            "test-a",
            async (_, token) =>
            {
                int now = Interlocked.Increment(ref concurrent);
                maxConcurrent = Math.Max(maxConcurrent, now);
                await Task.Delay(150, token);
                Interlocked.Decrement(ref concurrent);
                return 1;
            });

        Task<int> second = YFinanceRuntimeClientFactory.RunSerializedAsync(
            "test-b",
            async (_, token) =>
            {
                int now = Interlocked.Increment(ref concurrent);
                maxConcurrent = Math.Max(maxConcurrent, now);
                await Task.Delay(150, token);
                Interlocked.Decrement(ref concurrent);
                return 2;
            });

        int[] results = await Task.WhenAll(first, second);

        Assert.Equal(new[] { 1, 2 }, results);
        Assert.True(maxConcurrent >= 2, $"Expected concurrent client work, observed max concurrency {maxConcurrent}.");
        Assert.Equal(0, concurrent);
    }

    [Fact]
    public async Task YFinanceRuntimeClientFactory_DoesNotDisposeSharedClientWhileConcurrentOperationUsesIt()
    {
        using IDisposable serverBypass = YFinanceRuntimeClientFactory.SuppressServerStartupForTests();
        TaskCompletionSource<YFinanceServerClient> survivorClientSeen = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource failureObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<int> survivor = YFinanceRuntimeClientFactory.RunSerializedAsync(
            "test-survivor",
            async (client, token) =>
            {
                survivorClientSeen.SetResult(client);
                await failureObserved.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
                AssertYFinanceClientNotDisposed(client);
                return 1;
            });

        await survivorClientSeen.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Task<int> failing = YFinanceRuntimeClientFactory.RunSerializedAsync<int>(
            "test-failure",
            (_, _) => Task.FromException<int>(new InvalidOperationException("forced failure")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => failing);
        failureObserved.SetResult();

        Assert.Equal(1, await survivor);
    }

    [Fact]
    public async Task YFinanceRuntimeClientFactory_ResetRetiresSharedClientAfterActiveOperationCompletes()
    {
        using IDisposable serverBypass = YFinanceRuntimeClientFactory.SuppressServerStartupForTests();
        MethodInfo resetMethod = typeof(YFinanceRuntimeClientFactory).GetMethod(
            "ResetConnectionState",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        TaskCompletionSource<YFinanceServerClient> activeClientSeen = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource resetInvoked = new(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<int> activeOperation = YFinanceRuntimeClientFactory.RunSerializedAsync(
            "test-reset-active",
            async (client, token) =>
            {
                activeClientSeen.SetResult(client);
                await resetInvoked.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
                AssertYFinanceClientNotDisposed(client);
                return 1;
            });

        await activeClientSeen.Task.WaitAsync(TimeSpan.FromSeconds(5));
        resetMethod.Invoke(null, null);
        resetInvoked.SetResult();

        Assert.Equal(1, await activeOperation);
    }

    [Fact]
    public async Task YFinanceRuntimeClientFactory_TestSuppressionIsIsolatedPerAsyncFlow()
    {
        Assert.False(YFinanceRuntimeClientFactory.IsServerStartupSuppressedForTests);
        TaskCompletionSource suppressedFlowReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseSuppressedFlow = new(TaskCreationOptions.RunContinuationsAsynchronously);

        Task suppressedFlow = Task.Run(async () =>
        {
            using IDisposable serverBypass = YFinanceRuntimeClientFactory.SuppressServerStartupForTests();
            Assert.True(YFinanceRuntimeClientFactory.IsServerStartupSuppressedForTests);
            suppressedFlowReady.SetResult();
            await releaseSuppressedFlow.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(YFinanceRuntimeClientFactory.IsServerStartupSuppressedForTests);
        });

        await suppressedFlowReady.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(YFinanceRuntimeClientFactory.IsServerStartupSuppressedForTests);

        Task<bool> siblingFlow = Task.Run(() => YFinanceRuntimeClientFactory.IsServerStartupSuppressedForTests);
        bool siblingSuppressed = await siblingFlow.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(siblingSuppressed);

        releaseSuppressedFlow.SetResult();
        await suppressedFlow.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(YFinanceRuntimeClientFactory.IsServerStartupSuppressedForTests);
    }

    [Fact]
    public void DefaultHttpClients_ReuseSharedHandlers()
    {
        SocketsHttpHandler treasuryHandler = TreasuryYieldCurveQuoteProvider.SharedHttpHandlerForTests;
        Assert.Same(treasuryHandler, TreasuryYieldCurveQuoteProvider.SharedHttpHandlerForTests);
        Assert.Equal(TimeSpan.FromMinutes(5), treasuryHandler.PooledConnectionLifetime);
        Assert.Equal(TimeSpan.FromSeconds(30), treasuryHandler.PooledConnectionIdleTimeout);

        SocketsHttpHandler probeHandler = InternetProbeService.SharedProbeHandlerForTests;
        Assert.Same(probeHandler, InternetProbeService.SharedProbeHandlerForTests);
        Assert.Equal(TimeSpan.FromMinutes(5), probeHandler.PooledConnectionLifetime);
        Assert.Equal(TimeSpan.FromSeconds(30), probeHandler.PooledConnectionIdleTimeout);

        SocketsHttpHandler exchangeHandler = ExchangePhotoCacheService.DefaultHttpHandlerForTests;
        Assert.Same(exchangeHandler, ExchangePhotoCacheService.DefaultHttpHandlerForTests);
        Assert.Equal(TimeSpan.FromMinutes(5), exchangeHandler.PooledConnectionLifetime);
        Assert.Equal(TimeSpan.FromSeconds(30), exchangeHandler.PooledConnectionIdleTimeout);
    }

    [Fact]
    public async Task TickerInfoService_ResolvesUncachedSummariesWithBoundedParallelism()
    {
        string cacheRoot = Path.Combine(Path.GetTempPath(), "PortfolioSaverTests", Guid.NewGuid().ToString("N"));
        int concurrent = 0;
        int maxConcurrent = 0;
        int enteredSummaries = 0;
        object concurrencySync = new();
        TaskCompletionSource fourSummariesEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseSummaries = new(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            YFinanceOptions options = new() { PersistentCacheRootPath = cacheRoot };
            TickerInfoService service = new(
                (symbol, _) => Task.FromResult<YQuoteSnapshot?>(CreateYFinanceQuote(symbol)),
                (symbols, _) => Task.FromResult<IReadOnlyDictionary<string, YQuoteSnapshot>>(
                    symbols.ToDictionary(static symbol => symbol, CreateYFinanceQuote, StringComparer.Ordinal)),
                async (_, _, token) =>
                {
                    int now = Interlocked.Increment(ref concurrent);
                    lock (concurrencySync)
                    {
                        maxConcurrent = Math.Max(maxConcurrent, now);
                    }

                    if (Interlocked.Increment(ref enteredSummaries) == 4)
                        fourSummariesEntered.SetResult();

                    await releaseSummaries.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
                    Interlocked.Decrement(ref concurrent);
                    return null;
                },
                options);

            Task<IReadOnlyDictionary<string, YTickerInfo?>> infosTask = service.GetInfosAsync(["AAA", "BBB", "CCC", "DDD", "EEE", "FFF"]);

            await fourSummariesEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(4, maxConcurrent);
            releaseSummaries.SetResult();
            IReadOnlyDictionary<string, YTickerInfo?> infos = await infosTask.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(6, infos.Count);
            Assert.All(infos.Values, Assert.NotNull);
        }
        finally
        {
            if (Directory.Exists(cacheRoot))
                Directory.Delete(cacheRoot, recursive: true);
        }
    }

    [Fact]
    public async Task HybridHistoricalDataProvider_FetchesPendingSymbolsWithBoundedParallelism()
    {
        InMemoryHistoricalCacheService cache = new();
        int concurrent = 0;
        int maxConcurrent = 0;
        int enteredFetches = 0;
        object concurrencySync = new();
        TaskCompletionSource twoFetchesEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseFetches = new(TaskCreationOptions.RunContinuationsAsynchronously);
        HybridHistoricalDataProvider provider = new(
            cache,
            async (requestSymbol, _, _, _, _, token) =>
            {
                int now = Interlocked.Increment(ref concurrent);
                lock (concurrencySync)
                {
                    maxConcurrent = Math.Max(maxConcurrent, now);
                }

                if (Interlocked.Increment(ref enteredFetches) == 2)
                    twoFetchesEntered.SetResult();

                await releaseFetches.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
                Interlocked.Decrement(ref concurrent);
                return CreateHistoryResponse(requestSymbol);
            });

        Task<IReadOnlyList<TickerHistorySnapshot>> historyTask = provider.GetHistoryAsync(
            ["AAA", "BBB", "CCC", "DDD", "EEE", "FFF"],
            30);

        await twoFetchesEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, maxConcurrent);
        releaseFetches.SetResult();
        IReadOnlyList<TickerHistorySnapshot> snapshots = await historyTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(6, snapshots.Count);
        Assert.All(snapshots, snapshot => Assert.Single(snapshot.Points));
        Assert.Equal(2, maxConcurrent);
        Assert.Equal(6, cache.SavedCount);
    }

    [Fact]
    public async Task HybridHistoricalDataProvider_RetriesTransientHistoryFetch()
    {
        InMemoryHistoricalCacheService cache = new();
        int attempts = 0;
        List<string> operationIds = [];
        HybridHistoricalDataProvider provider = new(
            cache,
            (requestSymbol, _, _, _, operationId, _) =>
            {
                operationIds.Add(operationId);
                if (Interlocked.Increment(ref attempts) == 1)
                    return Task.FromException<HistoryResponseDto>(new HttpRequestException("transient"));

                return Task.FromResult(CreateHistoryResponse(requestSymbol));
            });

        IReadOnlyList<TickerHistorySnapshot> snapshots = await provider.GetHistoryAsync(["AAA"], 30);

        Assert.Equal(2, attempts);
        Assert.Equal(2, operationIds.Distinct(StringComparer.Ordinal).Count());
        TickerHistorySnapshot snapshot = Assert.Single(snapshots);
        Assert.Single(snapshot.Points);
        Assert.Equal(1, cache.SavedCount);
    }

    [Fact]
    public async Task HybridHistoricalDataProvider_FallsBackToStaleCacheWhenFetchFails()
    {
        InMemoryHistoricalCacheService cache = new();
        TickerHistorySnapshot stale = new()
        {
            Symbol = "AAA",
            LookbackDays = 30,
            FetchTimestampUtc = DateTimeOffset.UtcNow.AddDays(-2),
            Points = [new HistoricalPricePoint { TimestampUtc = DateTimeOffset.UtcNow.AddDays(-2), Close = 123m }]
        };
        cache.Seed(stale);
        HybridHistoricalDataProvider provider = new(
            cache,
            (_, _, _, _, _, _) => Task.FromException<HistoryResponseDto>(new InvalidOperationException("forced failure")),
            TimeSpan.FromMinutes(1));

        IReadOnlyList<TickerHistorySnapshot> snapshots = await provider.GetHistoryAsync(["AAA"], 30);

        TickerHistorySnapshot snapshot = Assert.Single(snapshots);
        Assert.Same(stale, snapshot);
    }

    [Fact]
    public void RuntimeQuoteSeedStore_PublishesAndConsumesQuotesOnce()
    {
        RuntimeQuoteSeedStore.ConsumeAll();
        RuntimeQuoteSeedStore.Publish(
        [
            new QuoteSnapshot { Symbol = "AAPL", Last = 190m, PreviousClose = 189m, FetchTimestampUtc = DateTimeOffset.UtcNow }
        ]);

        IReadOnlyDictionary<string, QuoteSnapshot> first = RuntimeQuoteSeedStore.ConsumeAll();
        IReadOnlyDictionary<string, QuoteSnapshot> second = RuntimeQuoteSeedStore.ConsumeAll();

        Assert.Single(first);
        Assert.True(first.ContainsKey("AAPL"));
        Assert.Empty(second);
    }

    [Fact]
    public void StartupCoordinator_NoLongerContainsDedicatedStartupWarmupPath()
    {
        string coordinatorPath = Path.Combine(
            GetRepoRoot(),
            "src", "PortfolioSaver.Presentation", "Services", "StartupCoordinator.cs");
        string source = File.ReadAllText(Path.GetFullPath(coordinatorPath));

        Assert.DoesNotContain("WarmStartupYahooQuotesAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StartupWarmupBatch", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetDedicatedYahooWarmupSymbols", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StartupCoordinator_DedicatedRuntimeRequestsUsePipelinedSingleSymbolQueue()
    {
        string coordinatorPath = Path.Combine(
            GetRepoRoot(),
            "src", "PortfolioSaver.Presentation", "Services", "StartupCoordinator.cs");
        string source = File.ReadAllText(Path.GetFullPath(coordinatorPath));

        Assert.Contains(
            "private List<string> TakeSequentialRequestSymbols(",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "private const int SequentialQuotePipelineDepth = 4;",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "QueueQuotePipelineRequests(orderedSymbols, yahooFinanceProvider, cancellationToken)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "DrainCompletedQuotePipelineAsync(",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("GetAwaiter().GetResult()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetQuotesAsync([symbol], CancellationToken.None)", source, StringComparison.Ordinal);
        Assert.Contains(
            "yahooFinanceProvider.GetQuotesAsync([symbol], cancellationToken)",
            source,
            StringComparison.Ordinal);
        Assert.Contains("SequentialQuoteCancelled", source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "IsDedicatedYahooSymbolCoolingDown",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "DataSourceSymbolEligibility.IsEligible(providerPlan.Kind, symbol)",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void StartupCoordinator_QuotePipelineAccessIsSerializedAndDrainedBeforeAwait()
    {
        string coordinatorPath = Path.Combine(
            GetRepoRoot(),
            "src", "PortfolioSaver.Presentation", "Services", "StartupCoordinator.cs");
        string source = File.ReadAllText(Path.GetFullPath(coordinatorPath));

        Assert.Contains("private readonly object _pendingQuotePipelineGate = new();", source, StringComparison.Ordinal);
        Assert.Contains("lock (_pendingQuotePipelineGate)", source, StringComparison.Ordinal);
        Assert.Contains("List<PendingQuoteRequest> completedRequests = [];", source, StringComparison.Ordinal);
        Assert.Contains("completedRequests.Add(pending);", source, StringComparison.Ordinal);
        Assert.Contains("foreach (PendingQuoteRequest pending in completedRequests)", source, StringComparison.Ordinal);
        Assert.Contains("IReadOnlyList<QuoteSnapshot> fetched = await pending.Task;", source, StringComparison.Ordinal);
        Assert.Contains("QuotePipelineSnapshot pipelineSnapshot = SnapshotQuotePipeline(orderedSymbols);", source, StringComparison.Ordinal);
        Assert.Contains("private QuotePipelineSnapshot SnapshotQuotePipeline(IReadOnlyList<string> orderedSymbols)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("orderedSymbols.Except(_pendingQuotePipeline.Keys", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartupCoordinator_ConcurrentQuotePipelineDrainsClaimEachCompletedRequestOnce()
    {
        StartupCoordinator coordinator = new();
        ControlledQuoteProvider provider = new();
        string[] symbols = ["AAPL", "MSFT", "VOO", "QUAL"];

        MethodInfo queueMethod = typeof(StartupCoordinator).GetMethod(
            "QueueQuotePipelineRequests",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        MethodInfo drainMethod = typeof(StartupCoordinator).GetMethod(
            "DrainCompletedQuotePipelineAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        FieldInfo pendingField = typeof(StartupCoordinator).GetField(
            "_pendingQuotePipeline",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        queueMethod.Invoke(coordinator, [symbols, provider, CancellationToken.None]);
        Assert.Equal(symbols.Length, provider.RequestedSymbols.Count);

        foreach (string symbol in symbols)
            provider.Complete(symbol, new QuoteSnapshot { Symbol = symbol, Last = 100m, FetchTimestampUtc = DateTimeOffset.UtcNow });

        using ManualResetEventSlim startGate = new(false);
        using CountdownEvent readyGate = new(8);
        Task<(int CompletedCount, int ResultCount)>[] drains = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(async () =>
            {
                readyGate.Signal();
                startGate.Wait();
                Dictionary<string, QuoteSnapshot> results = new(StringComparer.OrdinalIgnoreCase);
                object taskObject = drainMethod.Invoke(coordinator, [results])!;
                await (Task)taskObject;
                object drainResult = taskObject.GetType().GetProperty("Result")!.GetValue(taskObject)!;
                int completedCount = (int)drainResult.GetType().GetProperty("CompletedCount")!.GetValue(drainResult)!;
                return (completedCount, results.Count);
            }))
            .ToArray();

        Assert.True(readyGate.Wait(TimeSpan.FromSeconds(5)));
        startGate.Set();

        (int CompletedCount, int ResultCount)[] outcomes = await Task.WhenAll(drains);
        object pending = pendingField.GetValue(coordinator)!;
        int pendingCount = (int)pending.GetType().GetProperty("Count")!.GetValue(pending)!;

        Assert.Equal(symbols.Length, outcomes.Sum(outcome => outcome.CompletedCount));
        Assert.Equal(symbols.Length, outcomes.Sum(outcome => outcome.ResultCount));
        Assert.Single(outcomes.Where(outcome => outcome.CompletedCount > 0));
        Assert.Equal(0, pendingCount);
    }

    [Fact]
    public async Task StartupCoordinator_QuotePipelineLeavesIncompleteRequestsForLaterDrain()
    {
        StartupCoordinator coordinator = new();
        ControlledQuoteProvider provider = new();
        string[] symbols = ["AAPL", "MSFT", "VOO", "QUAL"];

        MethodInfo queueMethod = typeof(StartupCoordinator).GetMethod(
            "QueueQuotePipelineRequests",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        MethodInfo drainMethod = typeof(StartupCoordinator).GetMethod(
            "DrainCompletedQuotePipelineAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        FieldInfo pendingField = typeof(StartupCoordinator).GetField(
            "_pendingQuotePipeline",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        queueMethod.Invoke(coordinator, [symbols, provider, CancellationToken.None]);
        provider.Complete("AAPL", new QuoteSnapshot { Symbol = "AAPL", Last = 100m, FetchTimestampUtc = DateTimeOffset.UtcNow });
        provider.Complete("MSFT", new QuoteSnapshot { Symbol = "MSFT", Last = 200m, FetchTimestampUtc = DateTimeOffset.UtcNow });

        Dictionary<string, QuoteSnapshot> firstResults = new(StringComparer.OrdinalIgnoreCase);
        object firstTaskObject = drainMethod.Invoke(coordinator, [firstResults])!;
        await (Task)firstTaskObject;

        object pending = pendingField.GetValue(coordinator)!;
        int pendingAfterFirstDrain = (int)pending.GetType().GetProperty("Count")!.GetValue(pending)!;
        Assert.Equal(2, firstResults.Count);
        Assert.Equal(2, pendingAfterFirstDrain);

        provider.Complete("VOO", new QuoteSnapshot { Symbol = "VOO", Last = 300m, FetchTimestampUtc = DateTimeOffset.UtcNow });
        provider.Complete("QUAL", new QuoteSnapshot { Symbol = "QUAL", Last = 400m, FetchTimestampUtc = DateTimeOffset.UtcNow });

        Dictionary<string, QuoteSnapshot> secondResults = new(StringComparer.OrdinalIgnoreCase);
        object secondTaskObject = drainMethod.Invoke(coordinator, [secondResults])!;
        await (Task)secondTaskObject;

        int pendingAfterSecondDrain = (int)pending.GetType().GetProperty("Count")!.GetValue(pending)!;
        Assert.Equal(2, secondResults.Count);
        Assert.Equal(0, pendingAfterSecondDrain);
    }

    [Fact]
    public void ScreensaverRefreshTimer_UsesOneSecondAsyncQuoteDispatchPath()
    {
        string controlPath = Path.Combine(
            GetRepoRoot(),
            "src", "PortfolioSaver.Presentation", "Controls", "ScreensaverSceneControl.xaml.cs");
        string source = File.ReadAllText(Path.GetFullPath(controlPath));

        Assert.Contains(
            "_refreshTimer.Tick += (_, _) => DispatchNextRuntimeQuoteRequest();",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "await RefreshSceneAsync(preserveLayout: false, fullAncillaryRefresh: true);",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "RefreshSceneAsync bypassed progressive quote scene because the async runtime quote loop owns ordinary quote cadence.",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "Task<IReadOnlyList<QuoteSnapshot>> requestTask = _runtimeQuoteProvider.GetQuotesAsync([symbol], CancellationToken.None);",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "Dispatcher.InvokeAsync(() => ApplyCompletedRuntimeQuote(symbol, task))",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "RunStartupWarmupAsync",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "_refreshTimer.Tick += async (_, _) => await RefreshSceneAsync(preserveLayout: true, fullAncillaryRefresh: false);",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void NtpTimeService_BoundsDnsAndHostTimeouts()
    {
        string servicePath = Path.Combine(
            GetRepoRoot(),
            "src", "PortfolioSaver.Presentation", "Services", "NtpTimeService.cs");
        string source = File.ReadAllText(Path.GetFullPath(servicePath));

        Assert.Contains("DnsTimeout", source, StringComparison.Ordinal);
        Assert.Contains("PerHostTimeout", source, StringComparison.Ordinal);
        Assert.Contains("ResolveHostAsync(host, hostTimeout.Token)", source, StringComparison.Ordinal);
        Assert.Contains("Dns.GetHostAddressesAsync(host, dnsTimeout.Token)", source, StringComparison.Ordinal);
        Assert.Contains("catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)", source, StringComparison.Ordinal);
        Assert.Contains("HostTimeout", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProviderBudgetLedgerService_SerializesConcurrentReservationsAndPersistsValidLedger()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "PortfolioSaverTests", Guid.NewGuid().ToString("N"));
        string ledgerPath = Path.Combine(tempRoot, "provider-query-usage.json");
        try
        {
            ProviderBudgetLedgerService service = new(ledgerPath);
            DataSourcePolicySettings policy = new()
            {
                Kind = DataSourceKind.YahooFinance,
                MaxQueriesPerHour = 50,
                MaxQueriesPerDay = 50
            };
            DateTimeOffset nowUtc = DateTimeOffset.UtcNow;

            // Use one timestamp so task scheduling order cannot turn this lock/persistence test into a reuse-interval test.
            bool[] reservations = await Task.WhenAll(Enumerable.Range(0, 8)
                .Select(_ => Task.Run(() => service.TryReserve(policy, 1, TimeSpan.Zero, nowUtc))));

            Assert.Equal(8, reservations.Count(result => result));
            string json = File.ReadAllText(ledgerPath);
            using JsonDocument document = JsonDocument.Parse(json);
            JsonProperty entryProperty = Assert.Single(document.RootElement.GetProperty("Entries").EnumerateObject());
            Assert.Equal(8, entryProperty.Value.GetProperty("QueryTimestampsUtc").GetArrayLength());
            Assert.Equal(JsonValueKind.Null, entryProperty.Value.GetProperty("CooldownUntilUtc").ValueKind);
            Assert.DoesNotContain(".tmp", string.Join('|', Directory.EnumerateFiles(tempRoot).Select(Path.GetFileName)), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
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

    private static void AssertYFinanceClientNotDisposed(YFinanceServerClient client)
    {
        FieldInfo connectGateField = typeof(YFinanceServerClient).GetField(
            "_connectGate",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        SemaphoreSlim connectGate = (SemaphoreSlim)connectGateField.GetValue(client)!;

        Assert.True(connectGate.Wait(0));
        connectGate.Release();
    }

    private static HistoryResponseDto CreateHistoryResponse(string requestSymbol)
        => new(
            requestSymbol,
            [new HistoryBarDto(DateTimeOffset.UtcNow.AddDays(-1), 1m, 2m, 1m, 2m, 100)],
            new HistoryMetadataDto(null, null, "USD", "America/New_York", "EST", null, null, null),
            new CacheMetadataDto("test", 0, false));

    private static YQuoteSnapshot CreateYFinanceQuote(string symbol)
        => new(
            symbol,
            ShortName: symbol,
            LongName: symbol,
            DisplayName: symbol,
            Currency: "USD",
            Exchange: "TEST",
            ExchangeTimezoneName: "America/New_York",
            ExchangeTimezoneShortName: "EST",
            QuoteType: "EQUITY",
            MarketState: "REGULAR",
            RegularMarketPrice: 100m,
            RegularMarketPreviousClose: 99m,
            RegularMarketOpen: 99m,
            RegularMarketDayHigh: 101m,
            RegularMarketDayLow: 98m,
            RegularMarketChange: 1m,
            RegularMarketChangePercent: 1m,
            FiftyTwoWeekLow: 80m,
            FiftyTwoWeekHigh: 120m,
            FiftyDayAverage: 100m,
            TwoHundredDayAverage: 95m,
            RegularMarketVolume: 1000,
            AverageVolume: 1000,
            AverageVolume10Day: 1000,
            SharesOutstanding: 1000000,
            MarketCap: 100000000,
            TrailingPe: 20m,
            ForwardPe: 18m,
            Raw: JsonDocument.Parse("{}").RootElement.Clone());

    private sealed class ControlledQuoteProvider : IQuoteProvider
    {
        private readonly Dictionary<string, TaskCompletionSource<IReadOnlyList<QuoteSnapshot>>> _pending = new(StringComparer.OrdinalIgnoreCase);

        public List<string> RequestedSymbols { get; } = [];

        public Task<IReadOnlyList<QuoteSnapshot>> GetQuotesAsync(IEnumerable<string> symbols, CancellationToken cancellationToken = default)
        {
            string symbol = Assert.Single(symbols);
            RequestedSymbols.Add(symbol);
            TaskCompletionSource<IReadOnlyList<QuoteSnapshot>> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending.Add(symbol, completion);
            return completion.Task;
        }

        public Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public void Complete(string symbol, QuoteSnapshot quote)
            => _pending[symbol].SetResult([quote]);
    }

    private sealed class InMemoryHistoricalCacheService : IHistoricalCacheService
    {
        private readonly ConcurrentDictionary<string, TickerHistorySnapshot> _snapshots = new(StringComparer.OrdinalIgnoreCase);

        public int SavedCount => _snapshots.Count;

        public void Seed(TickerHistorySnapshot snapshot)
        {
            _snapshots[snapshot.Symbol] = snapshot;
        }

        public Task<TickerHistorySnapshot?> LoadAsync(string symbol, CancellationToken cancellationToken = default)
        {
            _snapshots.TryGetValue(symbol, out TickerHistorySnapshot? snapshot);
            return Task.FromResult(snapshot);
        }

        public Task SaveAsync(TickerHistorySnapshot snapshot, CancellationToken cancellationToken = default)
        {
            _snapshots[snapshot.Symbol] = snapshot;
            return Task.CompletedTask;
        }

        public Task PurgeExpiredAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}

