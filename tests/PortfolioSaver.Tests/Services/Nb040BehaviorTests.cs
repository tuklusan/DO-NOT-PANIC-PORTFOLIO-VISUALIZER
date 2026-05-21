using System.Reflection;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Data.Services;
using PortfolioSaver.Render.Services;
using PortfolioSaver.Screensaver.Services;
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
        YFinanceOptions options = new();

        Assert.Equal(TimeSpan.FromMinutes(10), options.DefaultCacheTtl);
        Assert.Equal(TimeSpan.FromMinutes(10), options.SummaryCacheTtl);
        Assert.Equal(TimeSpan.FromMinutes(10), options.PersistentMetadataCacheTtl);
        Assert.DoesNotContain("PortfolioSaver", options.UserAgent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Don't Panic", options.UserAgent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Visualiz", options.UserAgent, StringComparison.OrdinalIgnoreCase);
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
    public async Task YFinanceRuntimeClientFactory_SerializesConcurrentClientWork()
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
        Assert.Equal(1, maxConcurrent);
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
    public void StartupCoordinator_DedicatedRuntimeRequestsStaySingleSymbol()
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
            "return [selected];",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "!IsDedicatedYahooSymbol(symbol) || !IsDedicatedYahooSymbolCoolingDown(symbol, nowUtc)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "_sequentialRuntimeCursor = (_sequentialRuntimeCursor + 1) % eligibleSymbols.Count;",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "DataSourceSymbolEligibility.IsEligible(providerPlan.Kind, symbol)",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ScreensaverRefreshTimer_UsesProgressiveQuoteOnlyPath()
    {
        string controlPath = Path.Combine(
            GetRepoRoot(),
            "src", "PortfolioSaver.Presentation", "Controls", "ScreensaverSceneControl.xaml.cs");
        string source = File.ReadAllText(Path.GetFullPath(controlPath));

        Assert.Contains(
            "_refreshTimer.Tick += async (_, _) => await RefreshSceneAsync(preserveLayout: true, fullAncillaryRefresh: false);",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "await RefreshSceneAsync(preserveLayout: false, fullAncillaryRefresh: true);",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "RunStartupWarmupAsync",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "await _startupCoordinator.BuildProgressiveQuoteSceneAsync(currentRotationSeed)",
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

