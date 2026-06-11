using System.Reflection;
using System.Text.Json;
using PortfolioSaver.Core.Enums;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Data.Interfaces;
using PortfolioSaver.Data.Services;
using PortfolioSaver.Render.Services;
using PortfolioSaver.Screensaver.Services;
using PortfolioSaver.Shared.Helpers;
using Xunit;
using YFinance.NET.Config;
using YFinance.NET.Transport;

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
}

