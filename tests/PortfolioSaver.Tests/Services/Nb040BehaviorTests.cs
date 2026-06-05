using System.Reflection;
using PortfolioSaver.Core.Models;
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
            "QueueQuotePipelineRequests(",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "DrainCompletedQuotePipeline(",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "yahooFinanceProvider.GetQuotesAsync([symbol], CancellationToken.None)",
            source,
            StringComparison.Ordinal);
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

