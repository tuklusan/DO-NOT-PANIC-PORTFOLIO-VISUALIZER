using System.Collections;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using PortfolioSaver.Core.Constants;
using PortfolioSaver.Core.Enums;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Core.Services;
using PortfolioSaver.Data.Interfaces;
using PortfolioSaver.Data.Providers;
using PortfolioSaver.Data.Services;
using PortfolioSaver.Render.Controls;
using PortfolioSaver.Render.Services;
using PortfolioSaver.Render.ViewModels;
using PortfolioSaver.Screensaver.Services;
using PortfolioSaver.Shared.Helpers;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class StartupCoordinatorAdvancedTests
{
    [Fact]
    public async Task WarmStartupYahooQuotesAsync_WarmsDedicatedCrudeSymbolAlongsideDedicatedMacroLane()
    {
        RecordingQuoteProvider provider = new();
        List<TimeSpan> delays = [];
        StartupCoordinator coordinator = CreateCoordinatorWithIsolatedLedger(
            networkAvailability: () => true,
            yahooProviderFactory: _ => provider,
            delayAsync: (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

        AppSettings settings = Defaults.CreateSettings();
        settings.EnableFloatingClock = true;
        settings.Groups =
        [
            new TickerGroup
            {
                Name = "Tape 1",
                Direction = ScrollDirection.Left,
                Speed = 0.4,
                Enabled = true,
                Tickers =
                [
                    new TickerItem { Symbol = "AAPL", DisplayName = "AAPL", Enabled = true },
                    new TickerItem { Symbol = "MSFT", DisplayName = "MSFT", Enabled = true }
                ]
            }
        ];
        List<StartupWarmupBatch> batches = [];
        await foreach (StartupWarmupBatch batch in coordinator.WarmStartupYahooQuotesAsync(settings))
            batches.Add(batch);

        StartupWarmupBatch warmupBatch = Assert.Single(batches);
        IReadOnlyList<string> requested = Assert.Single(provider.BatchRequests);
        Assert.Single(requested);
        Assert.Equal("CL=F", requested[0]);
        Assert.Contains("CL=F", warmupBatch.Quotes.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Empty(delays);
    }

    [Fact]
    public async Task WarmStartupYahooQuotesAsync_WhenProbeUnavailable_StillAttemptsDedicatedCrudeWarmup()
    {
        RecordingQuoteProvider provider = new();
        StartupCoordinator coordinator = CreateCoordinatorWithIsolatedLedger(
            networkAvailability: () => false,
            yahooProviderFactory: _ => provider,
            delayAsync: (_, _) => Task.CompletedTask);

        AppSettings settings = Defaults.CreateSettings();
        settings.Groups =
        [
            new TickerGroup
            {
                Name = "Tape 1",
                Direction = ScrollDirection.Left,
                Speed = 0.4,
                Enabled = true,
                Tickers =
                [
                    new TickerItem { Symbol = "AAPL", DisplayName = "AAPL", Enabled = true }
                ]
            }
        ];

        List<StartupWarmupBatch> batches = [];
        await foreach (StartupWarmupBatch batch in coordinator.WarmStartupYahooQuotesAsync(settings))
            batches.Add(batch);

        StartupWarmupBatch warmupBatch = Assert.Single(batches);
        IReadOnlyList<string> requested = Assert.Single(provider.BatchRequests);
        Assert.Single(requested);
        Assert.Equal("CL=F", requested[0]);
        Assert.Contains("CL=F", warmupBatch.Quotes.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WarmStartupYahooQuotesAsync_WhenDedicatedCrudeWarmupRateLimits_StopsAfterSingleAttempt()
    {
        ThrowingQuoteProvider provider = new(new HttpRequestException("429 rate limited", null, System.Net.HttpStatusCode.TooManyRequests));
        List<TimeSpan> delays = [];
        StartupCoordinator coordinator = CreateCoordinatorWithIsolatedLedger(
            networkAvailability: () => true,
            yahooProviderFactory: _ => provider,
            delayAsync: (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

        AppSettings settings = Defaults.CreateSettings();
        settings.Groups =
        [
            new TickerGroup
            {
                Name = "Tape 1",
                Direction = ScrollDirection.Left,
                Speed = 0.4,
                Enabled = true,
                Tickers =
                [
                    new TickerItem { Symbol = "AAPL", DisplayName = "AAPL", Enabled = true },
                    new TickerItem { Symbol = "MSFT", DisplayName = "MSFT", Enabled = true }
                ]
            }
        ];
        List<StartupWarmupBatch> batches = [];
        await foreach (StartupWarmupBatch batch in coordinator.WarmStartupYahooQuotesAsync(settings))
            batches.Add(batch);

        Assert.Equal(1, provider.CallCount);
        Assert.Empty(batches);
        Assert.Empty(delays);
    }

    [Fact]
    public void BuildBootstrapScene_WhenNetworkAvailable_DoesNotShowFloatingWaitingOverlay()
    {
        string localDataRoot = CreateIsolatedLocalDataRoot();
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT", localDataRoot);
        try
        {
            StartupCoordinator coordinator = CreateCoordinatorWithIsolatedLedger(networkAvailability: () => true);

            ScreensaverSceneState scene = coordinator.BuildBootstrapScene();

            Assert.False(scene.ShowNetworkWaitingOverlay);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT", null);
        }
    }

    [Fact]
    public void BuildBootstrapScene_WhenNetworkUnavailable_ShowsFloatingWaitingOverlay()
    {
        string localDataRoot = CreateIsolatedLocalDataRoot();
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT", localDataRoot);
        try
        {
            StartupCoordinator coordinator = CreateCoordinatorWithIsolatedLedger(networkAvailability: () => false);

            ScreensaverSceneState scene = coordinator.BuildBootstrapScene();

            Assert.True(scene.ShowNetworkWaitingOverlay);
            Assert.Equal("Waiting for network", scene.NetworkWaitingTitle);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT", null);
        }
    }

    [Fact]
    public void BuildQuoteProviders_PrioritizesBackupsThenKeepsYahooAsLastResort()
    {
        AppSettings settings = Defaults.CreateSettings();
        settings.FinnhubApiKey = "finnhub-key";
        settings.TwelveDataApiKey = "twelve-key";
        settings.TiingoApiKey = "tiingo-key";

        MethodInfo method = GetBuildQuoteProvidersMethod();
        RecordingQuoteProvider provider = new();
        object? value = method.Invoke(
            null,
            [settings, provider, provider, provider, provider, 1]);

        IEnumerable plans = Assert.IsAssignableFrom<IEnumerable>(value);
        List<DataSourceKind> kinds = plans
            .Cast<object>()
            .Select(GetProviderKind)
            .ToList();

        Assert.Equal(
            [
                DataSourceKind.TwelveData,
                DataSourceKind.Tiingo,
                DataSourceKind.Finnhub,
                DataSourceKind.YahooFinance
            ],
            kinds);
    }

    [Fact]
    public void BuildQuoteProviders_WhenPoliciesDisableAllSources_StillKeepsYahooFallback()
    {
        AppSettings settings = Defaults.CreateSettings();
        settings.DataSources = [];
        settings.FinnhubApiKey = string.Empty;
        settings.TwelveDataApiKey = string.Empty;
        settings.TiingoApiKey = string.Empty;

        MethodInfo method = GetBuildQuoteProvidersMethod();
        RecordingQuoteProvider provider = new();
        object? value = method.Invoke(
            null,
            [settings, provider, provider, provider, provider, 0]);

        IEnumerable plans = Assert.IsAssignableFrom<IEnumerable>(value);
        List<DataSourceKind> kinds = plans
            .Cast<object>()
            .Select(GetProviderKind)
            .ToList();

        Assert.Single(kinds);
        Assert.Equal(DataSourceKind.YahooFinance, kinds[0]);
    }

    [Fact]
    public async Task LoadQuotesAsync_WhenYahooFails_FallsBackToBackupProvider()
    {
        AppSettings settings = Defaults.CreateSettings();
        settings.FinnhubApiKey = "finnhub-key";
        settings.TwelveDataApiKey = string.Empty;
        settings.TiingoApiKey = string.Empty;
        settings.DataSources =
        [
            DataSourceCatalog.CreateDefaultPolicy(DataSourceKind.YahooFinance),
            DataSourceCatalog.CreateDefaultPolicy(DataSourceKind.Finnhub)
        ];

        StartupCoordinator coordinator = CreateCoordinatorWithIsolatedLedger();
        ThrowingQuoteProvider yahooProvider = new(new InvalidOperationException("Yahoo unavailable"));
        StaticQuoteProvider finnhubProvider = new(
        [
            new QuoteSnapshot
            {
                Symbol = "AAPL",
                Last = 187.5m,
                ChangePercent = 0.5m,
                FetchTimestampUtc = DateTimeOffset.UtcNow
            }
        ]);
        StaticQuoteProvider emptyProvider = new([]);
        InMemoryQuoteCache cache = new([]);

        MethodInfo method = GetLoadQuotesAsyncMethod();
        Task task = (Task)(method.Invoke(
            coordinator,
            [
                (IReadOnlyList<string>)["AAPL"],
                (IReadOnlyList<string>)[],
                settings,
                true,
                finnhubProvider,
                emptyProvider,
                emptyProvider,
                yahooProvider,
                emptyProvider,
                cache,
                new ProviderHealthService(),
                (IReadOnlyDictionary<string, SymbolProfile>)new Dictionary<string, SymbolProfile>(StringComparer.OrdinalIgnoreCase),
                0,
                CancellationToken.None
            ]) ?? throw new InvalidOperationException("LoadQuotesAsync invocation failed."));

        await task;
        ITuple result = (ITuple)(task.GetType().GetProperty("Result")?.GetValue(task)
                         ?? throw new InvalidOperationException("LoadQuotesAsync result missing."));

        Dictionary<string, QuoteSnapshot> quotes = Assert.IsType<Dictionary<string, QuoteSnapshot>>(result[0]);
        string providerLabel = Assert.IsType<string>(result[1]);

        Assert.Equal(1, finnhubProvider.CallCount);
        Assert.True(quotes.ContainsKey("AAPL"));
        Assert.Contains("Finnhub", providerLabel, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Yahoo", providerLabel, StringComparison.OrdinalIgnoreCase);
        Assert.True(cache.SaveCalled);
        Assert.Equal(0, yahooProvider.CallCount);
    }

    [Fact]
    public async Task LoadQuotesAsync_WhenBackupsAlreadyProvideLiveData_DefersGeneralYahooTailToLaterPass()
    {
        AppSettings settings = Defaults.CreateSettings();
        settings.FinnhubApiKey = "finnhub-key";
        settings.TwelveDataApiKey = string.Empty;
        settings.TiingoApiKey = string.Empty;
        settings.DataSources =
        [
            DataSourceCatalog.CreateDefaultPolicy(DataSourceKind.YahooFinance),
            DataSourceCatalog.CreateDefaultPolicy(DataSourceKind.Finnhub)
        ];

        StartupCoordinator coordinator = CreateCoordinatorWithIsolatedLedger();
        RecordingQuoteProvider yahooProvider = new();
        StaticQuoteProvider finnhubProvider = new(
        [
            new QuoteSnapshot
            {
                Symbol = "AAPL",
                Last = 187.5m,
                ChangePercent = 0.5m,
                FetchTimestampUtc = DateTimeOffset.UtcNow
            },
            new QuoteSnapshot
            {
                Symbol = "MSFT",
                Last = 412.4m,
                ChangePercent = 0.7m,
                FetchTimestampUtc = DateTimeOffset.UtcNow
            },
            new QuoteSnapshot
            {
                Symbol = "GOOG",
                Last = 171.2m,
                ChangePercent = 0.6m,
                FetchTimestampUtc = DateTimeOffset.UtcNow
            },
            new QuoteSnapshot
            {
                Symbol = "AMZN",
                Last = 182.3m,
                ChangePercent = 0.4m,
                FetchTimestampUtc = DateTimeOffset.UtcNow
            }
        ]);
        StaticQuoteProvider emptyProvider = new([]);
        InMemoryQuoteCache cache = new([]);

        MethodInfo method = GetLoadQuotesAsyncMethod();
        Task task = (Task)(method.Invoke(
            coordinator,
            [
                (IReadOnlyList<string>)["AAPL", "MSFT", "GOOG", "AMZN", "NVDA", "META"],
                (IReadOnlyList<string>)[],
                settings,
                true,
                finnhubProvider,
                emptyProvider,
                emptyProvider,
                yahooProvider,
                emptyProvider,
                cache,
                new ProviderHealthService(),
                (IReadOnlyDictionary<string, SymbolProfile>)new Dictionary<string, SymbolProfile>(StringComparer.OrdinalIgnoreCase),
                0,
                CancellationToken.None
            ]) ?? throw new InvalidOperationException("LoadQuotesAsync invocation failed."));

        await task;
        ITuple result = (ITuple)(task.GetType().GetProperty("Result")?.GetValue(task)
                         ?? throw new InvalidOperationException("LoadQuotesAsync result missing."));

        Dictionary<string, QuoteSnapshot> quotes = Assert.IsType<Dictionary<string, QuoteSnapshot>>(result[0]);
        string providerLabel = Assert.IsType<string>(result[1]);

        Assert.Equal(1, finnhubProvider.CallCount);
        Assert.Empty(yahooProvider.BatchRequests);
        Assert.Equal(4, quotes.Count);
        Assert.Contains("Finnhub", providerLabel, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Yahoo", providerLabel, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("(Partial)", providerLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadQuotesAsync_WhenBackupsAreLive_SkipsRiskyYahooPartialRetryForRemainingGeneralSymbols()
    {
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        AppSettings settings = Defaults.CreateSettings();
        settings.FinnhubApiKey = "finnhub-key";
        settings.TwelveDataApiKey = string.Empty;
        settings.TiingoApiKey = string.Empty;
        settings.DataSources =
        [
            DataSourceCatalog.CreateDefaultPolicy(DataSourceKind.YahooFinance),
            DataSourceCatalog.CreateDefaultPolicy(DataSourceKind.Finnhub)
        ];

        StartupCoordinator coordinator = CreateCoordinatorWithIsolatedLedger();
        StaticQuoteProvider finnhubProvider = new(
        [
            new QuoteSnapshot
            {
                Symbol = "MSFT",
                Last = 412.4m,
                ChangePercent = 0.7m,
                FetchTimestampUtc = nowUtc.AddSeconds(2)
            }
        ]);
        PartialRateLimitedQuoteProvider yahooProvider = new(
            "Yahoo Finance rate limited (429) during chart retrieval.",
        [
            new QuoteSnapshot
            {
                Symbol = "AAPL",
                Last = 199.5m,
                ChangePercent = 0.4m,
                FetchTimestampUtc = nowUtc.AddSeconds(1)
            }
        ]);
        StaticQuoteProvider emptyProvider = new([]);
        InMemoryQuoteCache cache = new([]);

        MethodInfo method = GetLoadQuotesAsyncMethod();
        Task task = (Task)(method.Invoke(
            coordinator,
            [
                (IReadOnlyList<string>)["AAPL", "MSFT"],
                (IReadOnlyList<string>)[],
                settings,
                true,
                finnhubProvider,
                emptyProvider,
                emptyProvider,
                yahooProvider,
                emptyProvider,
                cache,
                new ProviderHealthService(),
                (IReadOnlyDictionary<string, SymbolProfile>)new Dictionary<string, SymbolProfile>(StringComparer.OrdinalIgnoreCase),
                0,
                CancellationToken.None
            ]) ?? throw new InvalidOperationException("LoadQuotesAsync invocation failed."));

        await task;
        ITuple result = (ITuple)(task.GetType().GetProperty("Result")?.GetValue(task)
                         ?? throw new InvalidOperationException("LoadQuotesAsync result missing."));

        Dictionary<string, QuoteSnapshot> quotes = Assert.IsType<Dictionary<string, QuoteSnapshot>>(result[0]);
        string providerLabel = Assert.IsType<string>(result[1]);

        Assert.Equal(0, yahooProvider.CallCount);
        Assert.Equal(1, finnhubProvider.CallCount);
        Assert.Single(quotes);
        Assert.Equal(412.4m, quotes["MSFT"].Last);
        Assert.Contains("Finnhub", providerLabel, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Yahoo", providerLabel, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("(Partial)", providerLabel, StringComparison.OrdinalIgnoreCase);
        Assert.True(cache.SaveCalled);
    }

    [Fact]
    public async Task LoadQuotesAsync_FiltersDedicatedYahooSymbolsOutOfBackupProviderRequests()
    {
        AppSettings settings = Defaults.CreateSettings();
        settings.FinnhubApiKey = "finnhub-key";
        settings.TwelveDataApiKey = "twelve-key";
        settings.TiingoApiKey = "tiingo-key";
        settings.DataSources =
        [
            DataSourceCatalog.CreateDefaultPolicy(DataSourceKind.YahooFinance),
            DataSourceCatalog.CreateDefaultPolicy(DataSourceKind.Finnhub),
            DataSourceCatalog.CreateDefaultPolicy(DataSourceKind.TwelveData),
            DataSourceCatalog.CreateDefaultPolicy(DataSourceKind.Tiingo)
        ];

        RecordingQuoteProvider globalMarketProvider = new();
        StartupCoordinator coordinator = CreateCoordinatorWithIsolatedLedger(globalMarketProviderFactory: _ => globalMarketProvider);
        ThrowingQuoteProvider yahooProvider = new(new HttpRequestException("429 rate limited", null, System.Net.HttpStatusCode.TooManyRequests));
        RecordingQuoteProvider finnhubProvider = new();
        RecordingQuoteProvider twelveDataProvider = new();
        RecordingQuoteProvider tiingoProvider = new();
        StaticQuoteProvider emptyProvider = new([]);
        InMemoryQuoteCache cache = new([]);

        MethodInfo method = GetLoadQuotesAsyncMethod();
        Task task = (Task)(method.Invoke(
            coordinator,
            [
                (IReadOnlyList<string>)["SPY"],
                (IReadOnlyList<string>)["DX-Y.NYB", "^FTSE"],
                settings,
                true,
                finnhubProvider,
                twelveDataProvider,
                tiingoProvider,
                yahooProvider,
                emptyProvider,
                cache,
                new ProviderHealthService(),
                (IReadOnlyDictionary<string, SymbolProfile>)new Dictionary<string, SymbolProfile>(StringComparer.OrdinalIgnoreCase),
                1,
                CancellationToken.None
            ]) ?? throw new InvalidOperationException("LoadQuotesAsync invocation failed."));

        await task;

        List<string> finnhubRequests =
        [
            .. finnhubProvider.BatchRequests.SelectMany(request => request)
        ];
        List<string> twelveDataRequests =
        [
            .. twelveDataProvider.BatchRequests.SelectMany(request => request)
        ];
        List<string> tiingoRequests =
        [
            .. tiingoProvider.BatchRequests.SelectMany(request => request)
        ];

        Assert.DoesNotContain("DX-Y.NYB", finnhubRequests, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("^FTSE", finnhubRequests, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("DX-Y.NYB", tiingoRequests, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("^FTSE", tiingoRequests, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("SPY", finnhubRequests.Concat(twelveDataRequests).Concat(tiingoRequests), StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("DX-Y.NYB", twelveDataRequests, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("DX-Y.NYB", globalMarketProvider.BatchRequests.SelectMany(request => request), StringComparer.OrdinalIgnoreCase);
        Assert.Contains("^FTSE", globalMarketProvider.BatchRequests.SelectMany(request => request), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadQuotesAsync_UsesGlobalMarketProviderBeforeYahooForSupportedWorldIndices()
    {
        AppSettings settings = Defaults.CreateSettings();
        settings.FinnhubApiKey = string.Empty;
        settings.TwelveDataApiKey = string.Empty;
        settings.TiingoApiKey = string.Empty;
        settings.DataSources =
        [
            DataSourceCatalog.CreateDefaultPolicy(DataSourceKind.YahooFinance)
        ];

        RecordingQuoteProvider globalMarketProvider = new();
        RecordingQuoteProvider yahooProvider = new();
        StaticQuoteProvider emptyProvider = new([]);
        InMemoryQuoteCache cache = new([]);
        StartupCoordinator coordinator = CreateCoordinatorWithIsolatedLedger(globalMarketProviderFactory: _ => globalMarketProvider);

        MethodInfo method = GetLoadQuotesAsyncMethod();
        Task task = (Task)(method.Invoke(
            coordinator,
            [
                (IReadOnlyList<string>)[],
                (IReadOnlyList<string>)["^FTSE", "^N225"],
                settings,
                true,
                emptyProvider,
                emptyProvider,
                emptyProvider,
                yahooProvider,
                emptyProvider,
                cache,
                new ProviderHealthService(),
                (IReadOnlyDictionary<string, SymbolProfile>)new Dictionary<string, SymbolProfile>(StringComparer.OrdinalIgnoreCase),
                0,
                CancellationToken.None
            ]) ?? throw new InvalidOperationException("LoadQuotesAsync invocation failed."));

        await task;

        List<string> globalRequests = [.. globalMarketProvider.BatchRequests.SelectMany(request => request)];
        Assert.Contains("^FTSE", globalRequests, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("^N225", globalRequests, StringComparer.OrdinalIgnoreCase);
        Assert.Empty(yahooProvider.BatchRequests);
    }

    [Fact]
    public async Task LoadQuotesAsync_PrioritizesMacroSymbolsBeforeWorldIndicesInDedicatedYahooLane()
    {
        AppSettings settings = Defaults.CreateSettings();
        settings.FinnhubApiKey = string.Empty;
        settings.TwelveDataApiKey = string.Empty;
        settings.TiingoApiKey = string.Empty;
        settings.DataSources =
        [
            DataSourceCatalog.CreateDefaultPolicy(DataSourceKind.YahooFinance)
        ];

        StartupCoordinator coordinator = CreateCoordinatorWithIsolatedLedger();
        RecordingQuoteProvider yahooProvider = new();
        StaticQuoteProvider emptyProvider = new([]);
        InMemoryQuoteCache cache = new([]);

        MethodInfo method = GetLoadQuotesAsyncMethod();
        Task task = (Task)(method.Invoke(
            coordinator,
            [
                (IReadOnlyList<string>)[],
                (IReadOnlyList<string>)["^SPX", "^FTSE", "DX-Y.NYB", "CL=F", "GC=F"],
                settings,
                true,
                emptyProvider,
                emptyProvider,
                emptyProvider,
                yahooProvider,
                emptyProvider,
                cache,
                new ProviderHealthService(),
                (IReadOnlyDictionary<string, SymbolProfile>)new Dictionary<string, SymbolProfile>(StringComparer.OrdinalIgnoreCase),
                0,
                CancellationToken.None
            ]) ?? throw new InvalidOperationException("LoadQuotesAsync invocation failed."));

        await task;

        IReadOnlyList<string> requested = Assert.Single(yahooProvider.BatchRequests);
        Assert.Single(requested);
        Assert.Contains(requested[0], new[] { "DX-Y.NYB", "CL=F", "GC=F" }, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadQuotesAsync_RotatesDedicatedYahooLaneAcrossRefreshPasses()
    {
        AppSettings settings = Defaults.CreateSettings();
        settings.FinnhubApiKey = string.Empty;
        settings.TwelveDataApiKey = string.Empty;
        settings.TiingoApiKey = string.Empty;
        settings.DataSources =
        [
            DataSourceCatalog.CreateDefaultPolicy(DataSourceKind.YahooFinance)
        ];

        MethodInfo method = GetLoadQuotesAsyncMethod();
        List<string> requestedSymbols = [];
        foreach (int refreshSeed in new[] { 0, 1, 2, 3, 4 })
        {
            StartupCoordinator coordinator = CreateCoordinatorWithIsolatedLedger();
            RecordingQuoteProvider yahooProvider = new();
            StaticQuoteProvider emptyProvider = new([]);
            InMemoryQuoteCache cache = new([]);

            Task task = (Task)(method.Invoke(
                coordinator,
                [
                    (IReadOnlyList<string>)[],
                (IReadOnlyList<string>)["^SPX", "^FTSE", "DX-Y.NYB", "CL=F", "GC=F"],
                    settings,
                    true,
                    emptyProvider,
                    emptyProvider,
                    emptyProvider,
                    yahooProvider,
                    emptyProvider,
                    cache,
                    new ProviderHealthService(),
                    (IReadOnlyDictionary<string, SymbolProfile>)new Dictionary<string, SymbolProfile>(StringComparer.OrdinalIgnoreCase),
                    refreshSeed,
                    CancellationToken.None
                ]) ?? throw new InvalidOperationException("LoadQuotesAsync invocation failed."));

            await task;
            requestedSymbols.Add(Assert.Single(Assert.Single(yahooProvider.BatchRequests)));
        }

        Assert.Equal(5, requestedSymbols.Count);
        Assert.Equal("DX-Y.NYB", requestedSymbols[0]);
        Assert.Contains("CL=F", requestedSymbols, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("GC=F", requestedSymbols, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("^SPX", requestedSymbols, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void TakeRequestSymbols_WhenDedicatedYahooSymbolIsCoolingDown_AdvancesToNextDedicatedSymbol()
    {
        AppSettings settings = Defaults.CreateSettings();
        settings.FinnhubApiKey = string.Empty;
        settings.TwelveDataApiKey = string.Empty;
        settings.TiingoApiKey = string.Empty;
        settings.DataSources =
        [
            DataSourceCatalog.CreateDefaultPolicy(DataSourceKind.YahooFinance)
        ];

        StartupCoordinator coordinator = CreateCoordinatorWithIsolatedLedger();
        MethodInfo buildMethod = GetBuildQuoteProvidersMethod();
        RecordingQuoteProvider provider = new();
        object yahooPlan = Assert.IsAssignableFrom<IEnumerable>(buildMethod.Invoke(
                null,
                [settings, provider, provider, provider, provider, 0]))
            .Cast<object>()
            .Single(plan => GetProviderKind(plan) == DataSourceKind.YahooFinance);

        MethodInfo applyRateLimitMethod = typeof(StartupCoordinator).GetMethod(
            "ApplyDedicatedYahooRateLimit",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("ApplyDedicatedYahooRateLimit method not found.");
        MethodInfo takeRequestMethod = typeof(StartupCoordinator).GetMethod(
            "TakeRequestSymbols",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("TakeRequestSymbols method not found.");

        DateTimeOffset nowUtc = new(2026, 4, 19, 4, 17, 0, TimeSpan.Zero);
        applyRateLimitMethod.Invoke(coordinator, [(IEnumerable<string>)["DX-Y.NYB"], nowUtc]);

        List<string> remainingSymbols = ["DX-Y.NYB", "CL=F", "GC=F", "^SPX", "^FTSE"];
        IReadOnlyDictionary<string, SymbolProfile> symbolProfiles = new Dictionary<string, SymbolProfile>(StringComparer.OrdinalIgnoreCase);

        List<string> requested = Assert.IsType<List<string>>(takeRequestMethod.Invoke(
            coordinator,
            [remainingSymbols, yahooPlan, symbolProfiles, 0, nowUtc])!);

        Assert.Single(requested);
        Assert.DoesNotContain("DX-Y.NYB", requested, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(requested[0], new[] { "CL=F", "GC=F", "^SPX", "^FTSE" }, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetMinimumProviderReuseInterval_ForDedicatedYahooRequests_UsesLongerProviderReuseWindow()
    {
        MethodInfo method = typeof(StartupCoordinator).GetMethod(
            "GetMinimumProviderReuseInterval",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("GetMinimumProviderReuseInterval method not found.");

        TimeSpan reuse = Assert.IsType<TimeSpan>(method.Invoke(null, [DataSourceKind.YahooFinance, (IReadOnlyList<string>)["DX-Y.NYB"]])!);

        Assert.Equal(TimeSpan.FromSeconds(180), reuse);
    }

    [Fact]
    public void GetRateLimitCooldown_ForDedicatedYahooRequests_UsesExtendedProviderCooldown()
    {
        MethodInfo method = typeof(StartupCoordinator).GetMethod(
            "GetRateLimitCooldown",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("GetRateLimitCooldown method not found.");

        TimeSpan cooldown = Assert.IsType<TimeSpan>(method.Invoke(null, [DataSourceKind.YahooFinance, (IReadOnlyList<string>)["^SPX"]])!);

        Assert.Equal(TimeSpan.FromMinutes(12), cooldown);
    }

    [Fact]
    public async Task LoadQuotesAsync_CapsTwelveDataBatchToStayWithinMinuteBudget()
    {
        AppSettings settings = Defaults.CreateSettings();
        settings.FinnhubApiKey = string.Empty;
        settings.TwelveDataApiKey = "twelve-key";
        settings.TiingoApiKey = string.Empty;
        settings.DataSources =
        [
            DataSourceCatalog.CreateDefaultPolicy(DataSourceKind.YahooFinance),
            DataSourceCatalog.CreateDefaultPolicy(DataSourceKind.TwelveData)
        ];

        StartupCoordinator coordinator = CreateCoordinatorWithIsolatedLedger();
        ThrowingQuoteProvider yahooProvider = new(new InvalidOperationException("Yahoo unavailable"));
        RecordingQuoteProvider twelveDataProvider = new();
        StaticQuoteProvider emptyProvider = new([]);
        InMemoryQuoteCache cache = new([]);

        MethodInfo method = GetLoadQuotesAsyncMethod();
        Task task = (Task)(method.Invoke(
            coordinator,
            [
                (IReadOnlyList<string>)["AAPL", "MSFT", "GOOG", "AMZN", "NVDA", "META", "TSLA", "AVGO"],
                (IReadOnlyList<string>)[],
                settings,
                true,
                emptyProvider,
                twelveDataProvider,
                emptyProvider,
                yahooProvider,
                emptyProvider,
                cache,
                new ProviderHealthService(),
                (IReadOnlyDictionary<string, SymbolProfile>)new Dictionary<string, SymbolProfile>(StringComparer.OrdinalIgnoreCase),
                0,
                CancellationToken.None
            ]) ?? throw new InvalidOperationException("LoadQuotesAsync invocation failed."));

        await task;

        IReadOnlyList<string> requested = Assert.Single(twelveDataProvider.BatchRequests);
        Assert.Equal(4, requested.Count);
    }

    [Fact]
    public void GetQueryCost_TwelveDataIncludesPerRequestMinuteOverhead()
    {
        AppSettings settings = Defaults.CreateSettings();
        settings.FinnhubApiKey = string.Empty;
        settings.TwelveDataApiKey = "twelve-key";
        settings.TiingoApiKey = string.Empty;

        MethodInfo buildMethod = GetBuildQuoteProvidersMethod();
        RecordingQuoteProvider provider = new();
        object? buildValue = buildMethod.Invoke(
            null,
            [settings, provider, provider, provider, provider, 0]);

        object providerPlan = Assert.IsAssignableFrom<IEnumerable>(buildValue)
            .Cast<object>()
            .Single(plan => GetProviderKind(plan) == DataSourceKind.TwelveData);

        MethodInfo method = typeof(StartupCoordinator).GetMethod(
            "GetQueryCost",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("StartupCoordinator.GetQueryCost not found.");

        int cost = Assert.IsType<int>(method.Invoke(null, [providerPlan, 7]));

        Assert.Equal(10, cost);
    }

    [Fact]
    public async Task LoadQuotesAsync_WhenProfilesExcludeYahoo_StillUsesYahooFallbackEligibility()
    {
        AppSettings settings = Defaults.CreateSettings();
        settings.FinnhubApiKey = string.Empty;
        settings.TwelveDataApiKey = string.Empty;
        settings.TiingoApiKey = string.Empty;
        settings.DataSources =
        [
            DataSourceCatalog.CreateDefaultPolicy(DataSourceKind.YahooFinance)
        ];

        StartupCoordinator coordinator = CreateCoordinatorWithIsolatedLedger();
        StaticQuoteProvider yahooProvider = new(
        [
            new QuoteSnapshot
            {
                Symbol = "AAPL",
                Last = 191.25m,
                ChangePercent = 0.4m,
                FetchTimestampUtc = DateTimeOffset.UtcNow
            }
        ]);
        StaticQuoteProvider emptyProvider = new([]);
        InMemoryQuoteCache cache = new([]);

        IReadOnlyDictionary<string, SymbolProfile> symbolProfiles = new Dictionary<string, SymbolProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["AAPL"] = new SymbolProfile
            {
                Symbol = "AAPL",
                SupportedQuoteSources = [DataSourceKind.Finnhub]
            }
        };

        MethodInfo method = GetLoadQuotesAsyncMethod();
        Task task = (Task)(method.Invoke(
            coordinator,
            [
                (IReadOnlyList<string>)["AAPL"],
                (IReadOnlyList<string>)[],
                settings,
                true,
                emptyProvider,
                emptyProvider,
                emptyProvider,
                yahooProvider,
                emptyProvider,
                cache,
                new ProviderHealthService(),
                symbolProfiles,
                0,
                CancellationToken.None
            ]) ?? throw new InvalidOperationException("LoadQuotesAsync invocation failed."));

        await task;
        ITuple result = (ITuple)(task.GetType().GetProperty("Result")?.GetValue(task)
                         ?? throw new InvalidOperationException("LoadQuotesAsync result missing."));
        Dictionary<string, QuoteSnapshot> quotes = Assert.IsType<Dictionary<string, QuoteSnapshot>>(result[0]);
        string providerLabel = Assert.IsType<string>(result[1]);

        Assert.Equal(1, yahooProvider.CallCount);
        Assert.True(quotes.ContainsKey("AAPL"));
        Assert.Contains("Yahoo", providerLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadQuotesAsync_DueCachedQuoteWithoutRefresh_DoesNotTurnStaleBeforeHardThreshold()
    {
        AppSettings settings = Defaults.CreateSettings();
        settings.DataSources =
        [
            DataSourceCatalog.CreateDefaultPolicy(DataSourceKind.YahooFinance)
        ];

        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        InMemoryQuoteCache cache = new(
        [
            new QuoteSnapshot
            {
                Symbol = "AAPL",
                Last = 188.12m,
                ChangePercent = 0.45m,
                FetchTimestampUtc = nowUtc - TimeSpan.FromMinutes(2)
            }
        ]);

        StartupCoordinator coordinator = CreateCoordinatorWithIsolatedLedger();
        StaticQuoteProvider emptyProvider = new([]);
        MethodInfo method = GetLoadQuotesAsyncMethod();
        Task task = (Task)(method.Invoke(
            coordinator,
            [
                (IReadOnlyList<string>)["AAPL"],
                (IReadOnlyList<string>)[],
                settings,
                true,
                emptyProvider,
                emptyProvider,
                emptyProvider,
                emptyProvider,
                emptyProvider,
                cache,
                new ProviderHealthService(),
                (IReadOnlyDictionary<string, SymbolProfile>)new Dictionary<string, SymbolProfile>(StringComparer.OrdinalIgnoreCase),
                0,
                CancellationToken.None
            ]) ?? throw new InvalidOperationException("LoadQuotesAsync invocation failed."));

        await task;
        ITuple result = (ITuple)(task.GetType().GetProperty("Result")?.GetValue(task)
                         ?? throw new InvalidOperationException("LoadQuotesAsync result missing."));
        Dictionary<string, QuoteSnapshot> quotes = Assert.IsType<Dictionary<string, QuoteSnapshot>>(result[0]);

        QuoteSnapshot quote = Assert.Single(quotes).Value;
        Assert.False(quote.IsStale);
        Assert.Equal(188.12m, quote.Last);
    }

    [Fact]
    public async Task LoadQuotesAsync_LowConfiguredRefreshRetriesSoonerWhileKeepingFreshCacheUsable()
    {
        AppSettings settings = Defaults.CreateSettings();
        settings.RefreshSecondsPortfolio = 60;
        settings.RefreshSecondsOffHours = 60;
        settings.DataSources =
        [
            DataSourceCatalog.CreateDefaultPolicy(DataSourceKind.YahooFinance)
        ];

        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        InMemoryQuoteCache cache = new(
        [
            new QuoteSnapshot
            {
                Symbol = "AAPL",
                Last = 188.12m,
                ChangePercent = 0.45m,
                FetchTimestampUtc = nowUtc - TimeSpan.FromMinutes(14)
            }
        ]);

        StartupCoordinator coordinator = CreateCoordinatorWithIsolatedLedger();
        StaticQuoteProvider yahooProvider = new([]);
        StaticQuoteProvider emptyProvider = new([]);
        MethodInfo method = GetLoadQuotesAsyncMethod();
        Task task = (Task)(method.Invoke(
            coordinator,
            [
                (IReadOnlyList<string>)["AAPL"],
                (IReadOnlyList<string>)[],
                settings,
                true,
                emptyProvider,
                emptyProvider,
                emptyProvider,
                yahooProvider,
                emptyProvider,
                cache,
                new ProviderHealthService(),
                (IReadOnlyDictionary<string, SymbolProfile>)new Dictionary<string, SymbolProfile>(StringComparer.OrdinalIgnoreCase),
                0,
                CancellationToken.None
            ]) ?? throw new InvalidOperationException("LoadQuotesAsync invocation failed."));

        await task;
        ITuple result = (ITuple)(task.GetType().GetProperty("Result")?.GetValue(task)
                         ?? throw new InvalidOperationException("LoadQuotesAsync result missing."));
        Dictionary<string, QuoteSnapshot> quotes = Assert.IsType<Dictionary<string, QuoteSnapshot>>(result[0]);

        QuoteSnapshot quote = Assert.Single(quotes).Value;
        Assert.False(quote.IsStale);
        Assert.Equal(1, yahooProvider.CallCount);
        Assert.Equal(188.12m, quote.Last);
    }

    [Fact]
    public async Task LoadQuotesAsync_HardStaleCachedQuote_RemainsStaleWhenRefreshMisses()
    {
        AppSettings settings = Defaults.CreateSettings();
        settings.DataSources =
        [
            DataSourceCatalog.CreateDefaultPolicy(DataSourceKind.YahooFinance)
        ];

        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        InMemoryQuoteCache cache = new(
        [
            new QuoteSnapshot
            {
                Symbol = "AAPL",
                Last = 188.12m,
                ChangePercent = 0.45m,
                FetchTimestampUtc = nowUtc - TimeSpan.FromMinutes(23)
            }
        ]);

        StartupCoordinator coordinator = CreateCoordinatorWithIsolatedLedger();
        StaticQuoteProvider emptyProvider = new([]);
        MethodInfo method = GetLoadQuotesAsyncMethod();
        Task task = (Task)(method.Invoke(
            coordinator,
            [
                (IReadOnlyList<string>)["AAPL"],
                (IReadOnlyList<string>)[],
                settings,
                true,
                emptyProvider,
                emptyProvider,
                emptyProvider,
                emptyProvider,
                emptyProvider,
                cache,
                new ProviderHealthService(),
                (IReadOnlyDictionary<string, SymbolProfile>)new Dictionary<string, SymbolProfile>(StringComparer.OrdinalIgnoreCase),
                0,
                CancellationToken.None
            ]) ?? throw new InvalidOperationException("LoadQuotesAsync invocation failed."));

        await task;
        ITuple result = (ITuple)(task.GetType().GetProperty("Result")?.GetValue(task)
                         ?? throw new InvalidOperationException("LoadQuotesAsync result missing."));
        Dictionary<string, QuoteSnapshot> quotes = Assert.IsType<Dictionary<string, QuoteSnapshot>>(result[0]);

        QuoteSnapshot quote = Assert.Single(quotes).Value;
        Assert.True(quote.IsStale);
        Assert.Equal(188.12m, quote.Last);
    }

    [Fact]
    public async Task LoadQuotesAsync_WhenNetworkUnavailable_RecentCachedQuoteRemainsVisible()
    {
        AppSettings settings = Defaults.CreateSettings();
        settings.DataSources =
        [
            DataSourceCatalog.CreateDefaultPolicy(DataSourceKind.YahooFinance)
        ];

        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        InMemoryQuoteCache cache = new(
        [
            new QuoteSnapshot
            {
                Symbol = "AAPL",
                Last = 188.12m,
                PreviousClose = 187.10m,
                ChangePercent = 0.45m,
                FetchTimestampUtc = nowUtc - TimeSpan.FromMinutes(3)
            }
        ]);

        StartupCoordinator coordinator = CreateCoordinatorWithIsolatedLedger();
        StaticQuoteProvider emptyProvider = new([]);
        MethodInfo method = GetLoadQuotesAsyncMethod();
        Task task = (Task)(method.Invoke(
            coordinator,
            [
                (IReadOnlyList<string>)["AAPL"],
                (IReadOnlyList<string>)[],
                settings,
                false,
                emptyProvider,
                emptyProvider,
                emptyProvider,
                emptyProvider,
                emptyProvider,
                cache,
                new ProviderHealthService(),
                (IReadOnlyDictionary<string, SymbolProfile>)new Dictionary<string, SymbolProfile>(StringComparer.OrdinalIgnoreCase),
                0,
                CancellationToken.None
            ]) ?? throw new InvalidOperationException("LoadQuotesAsync invocation failed."));

        await task;
        ITuple result = (ITuple)(task.GetType().GetProperty("Result")?.GetValue(task)
                         ?? throw new InvalidOperationException("LoadQuotesAsync result missing."));
        Dictionary<string, QuoteSnapshot> quotes = Assert.IsType<Dictionary<string, QuoteSnapshot>>(result[0]);

        QuoteSnapshot quote = Assert.Single(quotes).Value;
        Assert.False(quote.IsStale);
        Assert.Equal(188.12m, quote.Last);
    }

    [Fact]
    public async Task LoadQuotesAsync_WhenProbeUnavailable_StillUsesLiveProviderWhenReachable()
    {
        AppSettings settings = Defaults.CreateSettings();
        settings.DataSources =
        [
            DataSourceCatalog.CreateDefaultPolicy(DataSourceKind.YahooFinance)
        ];

        StartupCoordinator coordinator = CreateCoordinatorWithIsolatedLedger();
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        StaticQuoteProvider yahooProvider = new(
        [
            new QuoteSnapshot
            {
                Symbol = "AAPL",
                Last = 190.55m,
                ChangePercent = 0.3m,
                ProviderTimestampUtc = nowUtc - TimeSpan.FromDays(1),
                FetchTimestampUtc = nowUtc
            }
        ]);
        StaticQuoteProvider emptyProvider = new([]);
        InMemoryQuoteCache cache = new([]);

        MethodInfo method = GetLoadQuotesAsyncMethod();
        Task task = (Task)(method.Invoke(
            coordinator,
            [
                (IReadOnlyList<string>)["AAPL"],
                (IReadOnlyList<string>)[],
                settings,
                false,
                emptyProvider,
                emptyProvider,
                emptyProvider,
                yahooProvider,
                emptyProvider,
                cache,
                new ProviderHealthService(),
                (IReadOnlyDictionary<string, SymbolProfile>)new Dictionary<string, SymbolProfile>(StringComparer.OrdinalIgnoreCase),
                0,
                CancellationToken.None
            ]) ?? throw new InvalidOperationException("LoadQuotesAsync invocation failed."));

        await task;
        ITuple result = (ITuple)(task.GetType().GetProperty("Result")?.GetValue(task)
                         ?? throw new InvalidOperationException("LoadQuotesAsync result missing."));
        Dictionary<string, QuoteSnapshot> quotes = Assert.IsType<Dictionary<string, QuoteSnapshot>>(result[0]);
        string providerLabel = Assert.IsType<string>(result[1]);

        Assert.Equal(1, yahooProvider.CallCount);
        QuoteSnapshot quote = Assert.Single(quotes).Value;
        Assert.False(quote.IsStale);
        Assert.Equal(190.55m, quote.Last);
        Assert.Contains("Yahoo", providerLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadQuotesAsync_UsesFetchTimestampForStaleEvenWhenProviderTimestampIsOld()
    {
        AppSettings settings = Defaults.CreateSettings();
        settings.DataSources =
        [
            DataSourceCatalog.CreateDefaultPolicy(DataSourceKind.YahooFinance)
        ];

        StartupCoordinator coordinator = CreateCoordinatorWithIsolatedLedger();
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        StaticQuoteProvider yahooProvider = new(
        [
            new QuoteSnapshot
            {
                Symbol = "AAPL",
                Last = 190.01m,
                ChangePercent = 0.25m,
                ProviderTimestampUtc = nowUtc - TimeSpan.FromDays(2),
                FetchTimestampUtc = nowUtc
            }
        ]);
        StaticQuoteProvider emptyProvider = new([]);
        InMemoryQuoteCache cache = new([]);

        MethodInfo method = GetLoadQuotesAsyncMethod();
        Task task = (Task)(method.Invoke(
            coordinator,
            [
                (IReadOnlyList<string>)["AAPL"],
                (IReadOnlyList<string>)[],
                settings,
                true,
                emptyProvider,
                emptyProvider,
                emptyProvider,
                yahooProvider,
                emptyProvider,
                cache,
                new ProviderHealthService(),
                (IReadOnlyDictionary<string, SymbolProfile>)new Dictionary<string, SymbolProfile>(StringComparer.OrdinalIgnoreCase),
                0,
                CancellationToken.None
            ]) ?? throw new InvalidOperationException("LoadQuotesAsync invocation failed."));

        await task;
        ITuple result = (ITuple)(task.GetType().GetProperty("Result")?.GetValue(task)
                         ?? throw new InvalidOperationException("LoadQuotesAsync result missing."));
        Dictionary<string, QuoteSnapshot> quotes = Assert.IsType<Dictionary<string, QuoteSnapshot>>(result[0]);

        QuoteSnapshot quote = Assert.Single(quotes).Value;
        Assert.False(quote.IsStale);
        Assert.Equal(190.01m, quote.Last);
    }

    [Fact]
    public void DefaultAndNormalizedTapeDirections_AlternateLeftRight()
    {
        AppSettings defaults = Defaults.CreateSettings();
        Assert.Equal(
            [ScrollDirection.Left, ScrollDirection.Right, ScrollDirection.Left, ScrollDirection.Right],
            defaults.Groups.Take(4).Select(group => group.Direction).ToArray());

        AppSettings legacy = Defaults.CreateSettings();
        foreach (TickerGroup group in legacy.Groups)
            group.Direction = ScrollDirection.Left;

        AppSettings normalized = AppSettingsNormalizer.Normalize(legacy);
        Assert.Equal(
            [ScrollDirection.Left, ScrollDirection.Right, ScrollDirection.Left, ScrollDirection.Right],
            normalized.Groups.Take(4).Select(group => group.Direction).ToArray());
    }

    [Fact]
    public void BuildTapesForQuotes_RepeatsItemsToAvoidEmptyGaps()
    {
        AppSettings settings = Defaults.CreateSettings();
        settings.Groups =
        [
            new TickerGroup
            {
                Name = "Tape 1",
                Enabled = true,
                Direction = ScrollDirection.Left,
                Speed = 0.4d,
                Tickers =
                [
                    new TickerItem { Symbol = "AAPL", DisplayName = "Apple", Enabled = true },
                    new TickerItem { Symbol = "MSFT", DisplayName = "Microsoft", Enabled = true }
                ]
            }
        ];

        Dictionary<string, QuoteSnapshot> quotes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["AAPL"] = new QuoteSnapshot { Symbol = "AAPL", Last = 190m, ChangePercent = 1m, FetchTimestampUtc = DateTimeOffset.UtcNow },
            ["MSFT"] = new QuoteSnapshot { Symbol = "MSFT", Last = 380m, ChangePercent = -1m, FetchTimestampUtc = DateTimeOffset.UtcNow }
        };

        StartupCoordinator coordinator = CreateCoordinatorWithIsolatedLedger();
        IReadOnlyList<TapeViewModel> tapes = coordinator.BuildTapesForQuotes(settings, quotes);
        TapeViewModel tape = Assert.Single(tapes);

        Assert.True(tape.Items.Count >= 18);
        Assert.Equal("AAPL", tape.Items[0].SymbolText);
        Assert.Equal("MSFT", tape.Items[1].SymbolText);
        Assert.Equal("AAPL", tape.Items[2].SymbolText);
        Assert.Equal("MSFT", tape.Items[3].SymbolText);
    }

    [Fact]
    public void BuildTapesForQuotes_RotatesLaterCyclesForLongerLists()
    {
        AppSettings settings = Defaults.CreateSettings();
        settings.Groups =
        [
            new TickerGroup
            {
                Name = "Tape 1",
                Enabled = true,
                Direction = ScrollDirection.Left,
                Speed = 0.4d,
                Tickers =
                [
                    new TickerItem { Symbol = "S1", DisplayName = "S1", Enabled = true },
                    new TickerItem { Symbol = "S2", DisplayName = "S2", Enabled = true },
                    new TickerItem { Symbol = "S3", DisplayName = "S3", Enabled = true },
                    new TickerItem { Symbol = "S4", DisplayName = "S4", Enabled = true },
                    new TickerItem { Symbol = "S5", DisplayName = "S5", Enabled = true },
                    new TickerItem { Symbol = "S6", DisplayName = "S6", Enabled = true },
                    new TickerItem { Symbol = "S7", DisplayName = "S7", Enabled = true },
                    new TickerItem { Symbol = "S8", DisplayName = "S8", Enabled = true }
                ]
            }
        ];

        Dictionary<string, QuoteSnapshot> quotes = Enumerable.Range(1, 8)
            .ToDictionary(
                index => $"S{index}",
                index => new QuoteSnapshot
                {
                    Symbol = $"S{index}",
                    Last = 100m + index,
                    ChangePercent = 0.1m * index,
                    FetchTimestampUtc = DateTimeOffset.UtcNow
                },
                StringComparer.OrdinalIgnoreCase);

        StartupCoordinator coordinator = CreateCoordinatorWithIsolatedLedger();
        IReadOnlyList<TapeViewModel> tapes = coordinator.BuildTapesForQuotes(settings, quotes);
        TapeViewModel tape = Assert.Single(tapes);

        Assert.True(tape.Items.Count >= 18);
        Assert.Equal("S1", tape.Items[0].SymbolText);
        Assert.Equal("S8", tape.Items[7].SymbolText);
        Assert.Equal("S1", tape.Items[8].SymbolText);
        Assert.Equal("S5", tape.Items[16].SymbolText);
        Assert.Equal("S6", tape.Items[17].SymbolText);
    }

    [Fact]
    public void TickerTapeContentSignature_IgnoresValueOnlyUpdates()
    {
        TapeViewModel tape = new()
        {
            Items =
            [
                new TapeItemViewModel { SymbolText = "AAPL", LastText = "190.00", ChangeText = "+1.00%" },
                new TapeItemViewModel { SymbolText = "MSFT", LastText = "380.00", ChangeText = "-0.50%" }
            ]
        };

        MethodInfo method = typeof(TickerTapeControl).GetMethod(
            "BuildContentSignature",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("TickerTapeControl.BuildContentSignature not found.");

        string before = Assert.IsType<string>(method.Invoke(null, [tape]));
        tape.Items[0].LastText = "191.25";
        tape.Items[0].ChangeText = "+1.32%";
        string after = Assert.IsType<string>(method.Invoke(null, [tape]));

        Assert.Equal(before, after);
    }

    private static MethodInfo GetBuildQuoteProvidersMethod()
        => typeof(StartupCoordinator).GetMethod(
            "BuildQuoteProviders",
            BindingFlags.NonPublic | BindingFlags.Static)
           ?? throw new InvalidOperationException("StartupCoordinator.BuildQuoteProviders not found.");

    private static StartupCoordinator CreateCoordinatorWithIsolatedLedger(
        Func<bool>? networkAvailability = null,
        Func<HttpClient, IQuoteProvider>? yahooProviderFactory = null,
        Func<HttpClient, IQuoteProvider>? globalMarketProviderFactory = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), "PortfolioSaver.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        ProviderBudgetLedgerService ledger = new(Path.Combine(tempDirectory, "provider-query-usage.json"));
        return new StartupCoordinator(
            networkAvailability: networkAvailability,
            yahooProviderFactory: yahooProviderFactory,
            globalMarketProviderFactory: globalMarketProviderFactory ?? (_ => new StaticQuoteProvider([])),
            delayAsync: delayAsync,
            providerBudgetLedgerService: ledger);
    }

    private static string CreateIsolatedLocalDataRoot()
    {
        string root = Path.Combine(
            GetRepoRoot(),
            "build",
            "artifacts",
            "test-localdata",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static MethodInfo GetLoadQuotesAsyncMethod()
        => typeof(StartupCoordinator).GetMethod(
            "LoadQuotesAsync",
            BindingFlags.NonPublic | BindingFlags.Instance)
           ?? throw new InvalidOperationException("StartupCoordinator.LoadQuotesAsync not found.");

    private static DataSourceKind GetProviderKind(object providerExecutionPlan)
        => (DataSourceKind)(providerExecutionPlan.GetType().GetProperty("Kind")?.GetValue(providerExecutionPlan)
            ?? throw new InvalidOperationException("ProviderExecutionPlan.Kind missing."));

    private static string GetRepoRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, "PortfolioScreensaver.sln");
            if (File.Exists(candidate))
                return current.FullName;

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from test base directory.");
    }

    private sealed class RecordingQuoteProvider : IQuoteProvider
    {
        public List<IReadOnlyList<string>> BatchRequests { get; } = [];

        public Task<IReadOnlyList<QuoteSnapshot>> GetQuotesAsync(IEnumerable<string> symbols, CancellationToken cancellationToken = default)
        {
            List<string> requested = symbols.Where(symbol => !string.IsNullOrWhiteSpace(symbol)).ToList();
            BatchRequests.Add(requested);

            IReadOnlyList<QuoteSnapshot> quotes = requested
                .Select(symbol => new QuoteSnapshot
                {
                    Symbol = symbol,
                    Last = 100m,
                    ChangePercent = 0.1m,
                    FetchTimestampUtc = DateTimeOffset.UtcNow
                })
                .ToList();
            return Task.FromResult(quotes);
        }

        public Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }

    private sealed class ThrowingQuoteProvider(Exception exception) : IQuoteProvider
    {
        private readonly Exception _exception = exception;
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<QuoteSnapshot>> GetQuotesAsync(IEnumerable<string> symbols, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromException<IReadOnlyList<QuoteSnapshot>>(_exception);
        }

        public Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }

    private sealed class StaticQuoteProvider(IReadOnlyList<QuoteSnapshot> snapshots) : IQuoteProvider
    {
        private readonly IReadOnlyList<QuoteSnapshot> _snapshots = snapshots;
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<QuoteSnapshot>> GetQuotesAsync(IEnumerable<string> symbols, CancellationToken cancellationToken = default)
        {
            CallCount++;
            IReadOnlyList<QuoteSnapshot> filtered = _snapshots
                .Where(snapshot => symbols.Any(symbol => string.Equals(symbol, snapshot.Symbol, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            return Task.FromResult(filtered);
        }

        public Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }

    private sealed class PartialRateLimitedQuoteProvider(string message, IReadOnlyList<QuoteSnapshot> partialQuotes) : IQuoteProvider
    {
        private readonly string _message = message;
        private readonly IReadOnlyList<QuoteSnapshot> _partialQuotes = partialQuotes;
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<QuoteSnapshot>> GetQuotesAsync(IEnumerable<string> symbols, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromException<IReadOnlyList<QuoteSnapshot>>(new PartialQuoteResultException(_message, _partialQuotes));
        }

        public Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }

    private sealed class InMemoryQuoteCache(IReadOnlyList<QuoteSnapshot> snapshots) : IQuoteCacheService
    {
        private readonly IReadOnlyList<QuoteSnapshot> _snapshots = snapshots;
        public bool SaveCalled { get; private set; }

        public Task SaveAsync(IEnumerable<QuoteSnapshot> quotes, CancellationToken cancellationToken = default)
        {
            SaveCalled = true;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<QuoteSnapshot>> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_snapshots);
    }
}




