using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using PortfolioSaver.Core.Constants;
using PortfolioSaver.Core.Enums;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Core.Services;
using PortfolioSaver.Data.Interfaces;
using PortfolioSaver.Data.Providers;
using PortfolioSaver.Data.Services;
using PortfolioSaver.Media.Services;
using PortfolioSaver.Render.Services;
using PortfolioSaver.Render.ViewModels;
using PortfolioSaver.Shared.Diagnostics;
using PortfolioSaver.Shared.Helpers;

namespace PortfolioSaver.Screensaver.Services;

public sealed class StartupCoordinator
{
    private const int MinimumQuoteProviderReuseSeconds = 15;
    private const int YahooGeneralReuseSeconds = 60;
    private const int YahooDedicatedProviderReuseSeconds = 180;
    private const int YahooDedicatedSymbolCooldownMinutes = 12;
    private const int YahooGeneralRateLimitCooldownMinutes = 8;
    private const int YahooDedicatedRateLimitCooldownMinutes = 12;
    private const int YahooDedicatedWarmupBatchSize = 1;
    private const int YahooDedicatedWarmupMaxBatches = 1;
    private const int YahooDedicatedRuntimeBatchSymbols = 1;
    private const int YahooGeneralRuntimeBatchSymbols = 4;
    private const int TwelveDataPerRequestMinuteOverhead = 3;
    private const int TwelveDataMinuteSafetyReserve = 1;
    private const int TwelveDataAliasRuntimeBatchSymbols = 2;
    private static readonly TimeSpan TwelveDataReuseInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan YahooWarmupInterBatchDelay = TimeSpan.FromSeconds(YahooGeneralReuseSeconds);
    private const int MaxBatchSymbolsPerPass = 8;
    private const int MaxSequentialSymbolsPerPass = 4;
    private const int MinimumTapeItemCount = 18;
    private const int MinimumHeadlineCount = 20;
    private static readonly HashSet<string> DedicatedYahooSymbols = BuildDedicatedYahooSymbolSet();
    private static readonly HashSet<string> PreferredYahooWorldIndexSymbols = BuildPreferredYahooWorldIndexSet();
    private static readonly HashSet<string> OfficialMacroSymbols = BuildOfficialMacroSymbolSet();
    private static readonly HashSet<string> TreasuryMacroSymbols = BuildTreasuryMacroSymbolSet();

    private readonly ScreensaverSettingsService _settingsService = new();
    private readonly ExchangePhotoCacheService _exchangePhotoCacheService = new();
    private readonly NetworkAvailabilityService _networkAvailabilityService = new();
    private readonly NewYorkMarketStatusService _marketStatusService = new();
    private readonly ExchangeMarketCalendarService _exchangeMarketCalendarService = new();
    private readonly HistoricalGraphBuilder _historicalGraphBuilder = new();
    private readonly FloatingClockBuilder _floatingClockBuilder = new();
    private readonly FinanceNewsService _financeNewsService = new();
    private readonly ProviderBudgetLedgerService _providerBudgetLedgerService;
    private readonly SymbolProfileStore _symbolProfileStore = new(Path.Combine(PathHelper.GetLocalDataDirectory(), "symbol-profiles.json"));
    private readonly Dictionary<string, DateTimeOffset> _dedicatedYahooCooldownsUtc = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<bool> _isNetworkAvailable;
    private readonly Func<HttpClient, IQuoteProvider> _createYahooProvider;
    private readonly Func<HttpClient, IQuoteProvider> _createOfficialMacroProvider;
    private readonly Func<HttpClient, IQuoteProvider> _createGlobalMarketProvider;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;

    public StartupCoordinator(
        Func<bool>? networkAvailability = null,
        Func<HttpClient, IQuoteProvider>? yahooProviderFactory = null,
        Func<HttpClient, IQuoteProvider>? officialMacroProviderFactory = null,
        Func<HttpClient, IQuoteProvider>? globalMarketProviderFactory = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        ProviderBudgetLedgerService? providerBudgetLedgerService = null)
    {
        _isNetworkAvailable = networkAvailability ?? _networkAvailabilityService.IsNetworkAvailable;
        _createYahooProvider = yahooProviderFactory ?? (client => new YahooFinanceQuoteProvider(client));
        _createOfficialMacroProvider = officialMacroProviderFactory ?? (client => new CboeVolatilityIndexQuoteProvider(client));
        _createGlobalMarketProvider = globalMarketProviderFactory ?? (client => new StooqGlobalMarketQuoteProvider(client));
        _delayAsync = delayAsync ?? ((delay, cancellationToken) => Task.Delay(delay, cancellationToken));
        _providerBudgetLedgerService = providerBudgetLedgerService ?? new ProviderBudgetLedgerService();
        _marketStatusService.UpdateCalendarSnapshot(_exchangeMarketCalendarService.LoadNyseSnapshotFromCacheOrOffline());
    }

    public ScreensaverSceneState BuildBootstrapScene()
    {
        AppSettings settings = _settingsService.Load();
        bool networkAvailable = _isNetworkAvailable();
        IReadOnlyList<string> backgroundPaths = _exchangePhotoCacheService.GetImmediateBackgrounds(settings);
        QuoteCacheService quoteCacheService = new(Path.Combine(PathHelper.GetLocalDataDirectory(), "quotes-cache.json"));
        Dictionary<string, QuoteSnapshot> cachedQuotes = quoteCacheService.LoadCached()
            .GroupBy(quote => quote.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToDictionary(quote => quote.Symbol, StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<string> headlines = _financeNewsService.GetCachedHeadlines(settings.NewsScrollerMode);
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        bool showRecoveryOverlay = ShouldShowLiveRecoveryOverlay(cachedQuotes, settings, nowUtc);

        TraceRuntimeState(
            "BootstrapSceneBuilt",
            new KeyValuePair<string, object?>("network_available", networkAvailable),
            new KeyValuePair<string, object?>("background_count", backgroundPaths.Count),
            new KeyValuePair<string, object?>("cached_quote_count", cachedQuotes.Count),
            new KeyValuePair<string, object?>("headline_count", headlines.Count),
            new KeyValuePair<string, object?>("group_count", settings.Groups.Count(group => group.Enabled)),
            new KeyValuePair<string, object?>("show_recovery_overlay", showRecoveryOverlay));

        return new ScreensaverSceneState
        {
            Settings = settings,
            Quotes = cachedQuotes,
            Tapes = BuildTapeViewModels(settings, cachedQuotes),
            News = BuildNews(headlines),
            Status = new StatusBarViewModel
            {
                MarketStatusText = FormatStatusBandText(_marketStatusService.FormatStatusLine(DateTimeOffset.UtcNow)),
                ProviderText = networkAvailable
                    ? (showRecoveryOverlay ? "Provider: Refreshing stale cache" : (cachedQuotes.Count > 0 ? "Provider: Cache + live warmup" : "Provider: Loading live data"))
                    : (cachedQuotes.Count > 0 ? "Provider: Local Cache" : "Provider: Waiting for network"),
                UpdatedText = showRecoveryOverlay
                    ? "Updated: Refreshing live data..."
                    : (cachedQuotes.Count > 0 ? "Updated: Cache warm start" : "Updated: Starting..."),
                ClockDateText = DateTimeOffset.UtcNow.ToString("ddd dd-MMM", CultureInfo.InvariantCulture).ToUpperInvariant(),
                ClockText = $"{DateTimeOffset.UtcNow:HH:mm} UTC"
            },
            Graphs = [],
            Clock = settings.EnableFloatingClock ? _floatingClockBuilder.BuildDefault() : null,
            BackgroundPaths = backgroundPaths,
            ShowNetworkWaitingOverlay = !networkAvailable || showRecoveryOverlay,
            NetworkWaitingTitle = networkAvailable
                ? (showRecoveryOverlay ? "Refreshing live market data" : "Loading market data")
                : "Waiting for network",
            NetworkWaitingDetail = networkAvailable
                ? (showRecoveryOverlay
                    ? "Refreshing stale cached quotes before showing the live scene..."
                    : "Fetching live quotes, history, and exchange photos...")
                : $"Retrying live quotes and exchange photos every {Math.Max(5, (int)GetRefreshSeconds(settings))} seconds."
        };
    }

    public IReadOnlyList<TapeViewModel> BuildTapesForQuotes(AppSettings settings, IReadOnlyDictionary<string, QuoteSnapshot> quotes)
        => BuildTapeViewModels(settings, quotes);

    public async Task<ScreensaverSceneState> BuildSceneAsync(int graphRotationSeed = 0, CancellationToken cancellationToken = default)
    {
        AppSettings settings = _settingsService.Load();
        bool networkAvailable = _isNetworkAvailable();
        IReadOnlyDictionary<string, SymbolProfile> symbolProfiles = _symbolProfileStore.Load();

        using HttpClient httpClient = HttpClientFactory.Create(TimeSpan.FromSeconds(Math.Max(3, settings.HttpTimeoutSeconds)));
        IQuoteCacheService quoteCacheService = new QuoteCacheService(Path.Combine(PathHelper.GetLocalDataDirectory(), "quotes-cache.json"));
        ProviderHealthService providerHealthService = new();
        IQuoteProvider finnhubProvider = new FinnhubQuoteProvider(httpClient, settings.FinnhubApiKey);
        IQuoteProvider twelveDataProvider = new TwelveDataQuoteProvider(httpClient, settings.TwelveDataApiKey);
        IQuoteProvider tiingoProvider = new TiingoQuoteProvider(httpClient, settings.TiingoApiKey);
        IQuoteProvider yahooFinanceProvider = new YahooFinanceQuoteProvider(httpClient);
        IQuoteProvider treasuryYieldProvider = new TreasuryYieldCurveQuoteProvider(httpClient);

        List<string> portfolioSymbols = BuildInterleavedPortfolioSymbols(settings);
        List<string> ancillarySymbols =
        [
            .. FloatingClockBuilder.GetWorldIndexSymbols(),
            .. GetMacroIndicatorSymbols()
        ];

        Task<(Dictionary<string, QuoteSnapshot> Quotes, string ProviderLabel)> quotesTask = LoadQuotesAsync(
            portfolioSymbols,
            ancillarySymbols,
            settings,
            networkAvailable,
            finnhubProvider,
            twelveDataProvider,
            tiingoProvider,
            yahooFinanceProvider,
            treasuryYieldProvider,
            quoteCacheService,
            providerHealthService,
            symbolProfiles,
            graphRotationSeed,
            cancellationToken);
        Task<IReadOnlyList<string>> backgroundsTask = _exchangePhotoCacheService.GetAvailableBackgroundsAsync(
            settings,
            httpClient,
            networkAvailable,
            cancellationToken);
        Task<IReadOnlyList<string>> headlinesTask = _financeNewsService.GetHeadlinesAsync(
            httpClient,
            settings,
            networkAvailable,
            cancellationToken);

        await Task.WhenAll(quotesTask, backgroundsTask, headlinesTask);

        (Dictionary<string, QuoteSnapshot> quotes, string providerLabel) = await quotesTask;
        IReadOnlyList<string> backgroundPaths = await backgroundsTask;
        IReadOnlyList<string> headlines = await headlinesTask;

        List<TapeViewModel> tapes = BuildTapeViewModels(settings, quotes);
        NewsFlasherViewModel news = BuildNews(headlines);
        FloatingClockViewModel? clock = settings.EnableFloatingClock ? _floatingClockBuilder.BuildDefault() : null;

        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        DateTimeOffset lastUpdate = quotes.Values
            .Select(quote => quote.FetchTimestampUtc)
            .DefaultIfEmpty(nowUtc)
            .Max();
        bool showRecoveryOverlay = ShouldShowLiveRecoveryOverlay(quotes, settings, nowUtc);

        StatusBarViewModel status = new()
        {
            MarketStatusText = FormatStatusBandText(_marketStatusService.FormatStatusLine(nowUtc)),
            ProviderText = showRecoveryOverlay
                ? "Provider: Refreshing stale cache"
                : $"Provider: {providerLabel}",
            UpdatedText = showRecoveryOverlay
                ? "Updated: Refreshing live data..."
                : $"Updated: {TimeFormatHelper.ToAgeString(lastUpdate)}",
            ClockDateText = nowUtc.ToString("ddd dd-MMM", CultureInfo.InvariantCulture).ToUpperInvariant(),
            ClockText = $"{nowUtc:HH:mm} UTC"
        };

        bool showNetworkWaitingOverlay = ShouldShowNetworkWaitingOverlay(networkAvailable, providerLabel) || showRecoveryOverlay;

        return new ScreensaverSceneState
        {
            Settings = settings,
            Quotes = quotes,
            Tapes = tapes,
            News = news,
            Status = status,
            Graphs = [],
            Clock = clock,
            BackgroundPaths = backgroundPaths,
            ShowNetworkWaitingOverlay = showNetworkWaitingOverlay,
            NetworkWaitingTitle = showRecoveryOverlay ? "Refreshing live market data" : "Waiting for network",
            NetworkWaitingDetail = showRecoveryOverlay
                ? "Holding the scene until enough fresh quotes arrive..."
                : $"Retrying live quotes and exchange photos every {Math.Max(5, (int)GetRefreshSeconds(settings))} seconds."
        };
    }

    public async IAsyncEnumerable<StartupWarmupBatch> WarmStartupYahooQuotesAsync(
        AppSettings settings,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        bool networkAvailable = _isNetworkAvailable();
        if (!networkAvailable)
            TraceRuntime("Warmup proceeding despite unavailable network probe; attempting Yahoo batches opportunistically.");

        List<string> symbols = GetDedicatedYahooWarmupSymbols(settings)
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (symbols.Count == 0)
            yield break;

        using HttpClient httpClient = HttpClientFactory.Create(TimeSpan.FromSeconds(Math.Max(3, settings.HttpTimeoutSeconds)));
        IQuoteProvider yahooProvider = _createYahooProvider(httpClient);
        IQuoteCacheService quoteCacheService = new QuoteCacheService(Path.Combine(PathHelper.GetLocalDataDirectory(), "quotes-cache.json"));
        Dictionary<string, QuoteSnapshot> aggregated = new(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<List<string>> batches = ChunkSymbols(symbols, YahooDedicatedWarmupBatchSize)
            .Take(YahooDedicatedWarmupMaxBatches)
            .ToList();

        TraceRuntimeState(
            "WarmupPlan",
            new KeyValuePair<string, object?>("symbol_count", symbols.Count),
            new KeyValuePair<string, object?>("batch_count", batches.Count),
            new KeyValuePair<string, object?>("symbols", PreviewSymbols(symbols)),
            new KeyValuePair<string, object?>("network_probe_available", networkAvailable));

        for (int batchIndex = 0; batchIndex < batches.Count; batchIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<string> batchSymbols = batches[batchIndex];
            TraceRuntimeState(
                "WarmupBatchStarting",
                new KeyValuePair<string, object?>("batch_number", batchIndex + 1),
                new KeyValuePair<string, object?>("batch_total", batches.Count),
                new KeyValuePair<string, object?>("symbols", PreviewSymbols(batchSymbols)),
                new KeyValuePair<string, object?>("aggregated_quote_count", aggregated.Count));
            bool haltAfterBatch = false;
            try
            {
                IReadOnlyList<QuoteSnapshot> fetched = await yahooProvider.GetQuotesAsync(batchSymbols, cancellationToken);
                foreach (QuoteSnapshot quote in fetched)
                    aggregated[quote.Symbol] = quote;

                if (aggregated.Count > 0)
                    await quoteCacheService.SaveAsync(aggregated.Values, cancellationToken);

                TraceRuntimeState(
                    "WarmupBatchCompleted",
                    new KeyValuePair<string, object?>("batch_number", batchIndex + 1),
                    new KeyValuePair<string, object?>("batch_total", batches.Count),
                    new KeyValuePair<string, object?>("fetched_count", fetched.Count),
                    new KeyValuePair<string, object?>("aggregated_quote_count", aggregated.Count),
                    new KeyValuePair<string, object?>("aggregated_symbols", PreviewSymbols(aggregated.Keys)));
            }
            catch (Exception ex)
            {
                if (TryGetPartialQuotes(ex, out IReadOnlyList<QuoteSnapshot>? partialQuotes))
                {
                    IReadOnlyList<QuoteSnapshot> appliedPartialQuotes = partialQuotes!;
                    foreach (QuoteSnapshot quote in appliedPartialQuotes)
                        aggregated[quote.Symbol] = quote;

                    if (aggregated.Count > 0)
                        await quoteCacheService.SaveAsync(aggregated.Values, cancellationToken);

                    TraceRuntimeState(
                        "WarmupBatchPartialQuotesApplied",
                        new KeyValuePair<string, object?>("batch_number", batchIndex + 1),
                        new KeyValuePair<string, object?>("partial_count", appliedPartialQuotes.Count),
                        new KeyValuePair<string, object?>("partial_symbols", PreviewSymbols(appliedPartialQuotes.Select(quote => quote.Symbol))),
                        new KeyValuePair<string, object?>("aggregated_quote_count", aggregated.Count));
                }

                TraceRuntime($"Startup Yahoo warmup batch {batchIndex + 1}/{batches.Count} failed: {ex.GetType().Name}: {ex.Message}");
                if (IsRateLimited(ex))
                {
                    DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
                    IReadOnlyList<string> failedBatch = batches[batchIndex];
                    ApplyDedicatedYahooRateLimit(failedBatch, nowUtc);
                    _providerBudgetLedgerService.NoteRateLimit(DataSourceKind.YahooFinance, GetRateLimitCooldown(DataSourceKind.YahooFinance, failedBatch), nowUtc);
                    TraceRuntime("Startup Yahoo warmup halted due to rate limiting; deferring retries to scheduled provider refresh/fallback flow.");
                    haltAfterBatch = true;
                }
            }

            if (aggregated.Count > 0 || !haltAfterBatch)
            {
                yield return new StartupWarmupBatch(
                    new Dictionary<string, QuoteSnapshot>(aggregated, StringComparer.OrdinalIgnoreCase),
                    batchIndex + 1,
                    batches.Count,
                    $"Warmup {batchIndex + 1}/{batches.Count} batches complete via Yahoo Finance");
            }

            if (haltAfterBatch)
                break;

            if (batchIndex < batches.Count - 1)
                await _delayAsync(YahooWarmupInterBatchDelay, cancellationToken);
        }
    }

    public async IAsyncEnumerable<FloatingGraphViewModel> LoadGraphsIncrementallyAsync(
        AppSettings settings,
        int graphRotationSeed,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        const int graphLookbackDays = 1;

        if (!settings.EnableFloatingGraphs)
            yield break;

        bool networkAvailable = _isNetworkAvailable();
        IReadOnlyDictionary<string, SymbolProfile> symbolProfiles = _symbolProfileStore.Load();
        using HttpClient httpClient = HttpClientFactory.Create(TimeSpan.FromSeconds(Math.Max(3, settings.HttpTimeoutSeconds)));
        IHistoricalCacheService cacheService = new HistoricalCacheService(settings.HistoricalCacheRootFolder);
        IHistoricalDataProvider historicalProvider = new HybridHistoricalDataProvider(
            cacheService,
            httpClient,
            settings.FinnhubApiKey,
            settings.TwelveDataApiKey,
            TimeSpan.FromHours(Math.Max(1, settings.HistoricalRefreshHours)),
            TimeSpan.FromSeconds(Math.Max(1, settings.MinFinnhubRequestSpacingSeconds)),
            TimeSpan.FromSeconds(Math.Max(1, settings.MinTwelveDataRequestSpacingSeconds)),
            graphRotationSeed,
            symbolProfiles);

        List<(TickerGroup Group, TickerItem Ticker)> graphPairs = SelectGraphTickerPairs(settings, graphRotationSeed).ToList();
        Dictionary<string, TickerHistorySnapshot> cachedBySymbol = new(StringComparer.OrdinalIgnoreCase);
        List<string> liveFetchSymbols = [];

        foreach ((TickerGroup group, TickerItem ticker) in graphPairs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TraceGraph($"Graph warmup checking {ticker.Symbol} on {group.Name}.");

            TickerHistorySnapshot? cached = await cacheService.LoadAsync(ticker.Symbol, cancellationToken);
            if (cached is not null && cached.LookbackDays == graphLookbackDays && cached.Points.Count >= 2)
            {
                cachedBySymbol[ticker.Symbol] = cached;
                TraceGraph($"Graph warmup using cache for {ticker.Symbol} with {cached.Points.Count} points.");
                yield return BuildGraph(group.Name, cached, settings);
            }

            if (!networkAvailable)
            {
                TraceGraph($"Graph warmup skipped live fetch for {ticker.Symbol} because network is unavailable.");
                continue;
            }

            if (!HasEnabledHistorySource(ticker.Symbol, settings, symbolProfiles))
            {
                TraceGraph($"Graph warmup skipped live fetch for {ticker.Symbol} because no supported history source is known.");
                continue;
            }

            if (!liveFetchSymbols.Contains(ticker.Symbol, StringComparer.OrdinalIgnoreCase))
                liveFetchSymbols.Add(ticker.Symbol);
        }

        if (!networkAvailable || liveFetchSymbols.Count == 0)
            yield break;

        IReadOnlyList<TickerHistorySnapshot> refreshedSnapshots = await historicalProvider.GetHistoryAsync(
            liveFetchSymbols,
            graphLookbackDays,
            cancellationToken);
        Dictionary<string, TickerHistorySnapshot> refreshedBySymbol = refreshedSnapshots
            .Where(snapshot => snapshot.Points.Count >= 2)
            .GroupBy(snapshot => snapshot.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToDictionary(snapshot => snapshot.Symbol, StringComparer.OrdinalIgnoreCase);

        foreach ((TickerGroup group, TickerItem ticker) in graphPairs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!refreshedBySymbol.TryGetValue(ticker.Symbol, out TickerHistorySnapshot? refreshed))
            {
                TraceGraph($"Graph warmup received insufficient live history for {ticker.Symbol}.");
                continue;
            }

            if (cachedBySymbol.TryGetValue(ticker.Symbol, out TickerHistorySnapshot? cached) &&
                SnapshotsEquivalent(cached, refreshed))
            {
                TraceGraph($"Graph warmup live history for {ticker.Symbol} matched cache.");
                continue;
            }

            TraceGraph($"Graph warmup fetched live history for {ticker.Symbol} with {refreshed.Points.Count} points.");
            yield return BuildGraph(group.Name, refreshed, settings);
        }
    }

    private async Task<(Dictionary<string, QuoteSnapshot> Quotes, string ProviderLabel)> LoadQuotesAsync(
        IReadOnlyList<string> portfolioSymbols,
        IReadOnlyList<string> benchmarkSymbols,
        AppSettings settings,
        bool networkAvailable,
        IQuoteProvider finnhubProvider,
        IQuoteProvider twelveDataProvider,
        IQuoteProvider tiingoProvider,
        IQuoteProvider yahooFinanceProvider,
        IQuoteProvider treasuryYieldProvider,
        IQuoteCacheService quoteCacheService,
        ProviderHealthService providerHealthService,
        IReadOnlyDictionary<string, SymbolProfile> symbolProfiles,
        int refreshSeed,
        CancellationToken cancellationToken)
    {
        List<string> orderedSymbols = RotateSymbols(
        [
            .. portfolioSymbols,
            .. benchmarkSymbols
        ], refreshSeed);
        Dictionary<string, QuoteSnapshot> cachedQuotes = (await quoteCacheService.LoadAsync(cancellationToken))
            .GroupBy(quote => quote.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToDictionary(quote => quote.Symbol, StringComparer.OrdinalIgnoreCase);
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;

        if (!networkAvailable)
            TraceRuntime($"Network probe unavailable. Attempting opportunistic live quote refresh. Cached={cachedQuotes.Count}");

        Dictionary<string, QuoteSnapshot> results = cachedQuotes.ToDictionary(
            pair => pair.Key,
            pair => CloneQuote(pair.Value),
            StringComparer.OrdinalIgnoreCase);
        HashSet<string> liveProvidersUsed = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> refreshedSymbols = new(StringComparer.OrdinalIgnoreCase);
        string? latestUpdatedSymbol = null;
        DateTimeOffset latestUpdatedFetchUtc = DateTimeOffset.MinValue;
        Dictionary<string, TimeSpan> refreshWindows = BuildRefreshWindows(settings, portfolioSymbols, benchmarkSymbols);
        TimeSpan refreshPollingInterval = QuoteRefreshPolicy.GetRefreshPollingInterval(settings, nowUtc);
        HashSet<string> dueSymbols = orderedSymbols
            .Where(symbol => refreshWindows.TryGetValue(symbol, out TimeSpan refreshWindow) && IsRefreshDue(symbol, refreshWindow, cachedQuotes, nowUtc))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<string> scheduledDueSymbols = SelectDueSymbolsForPass(
            orderedSymbols,
            dueSymbols,
            refreshWindows,
            cachedQuotes,
            nowUtc,
            refreshPollingInterval);
        List<string> preemptiveRefreshSymbols = [];
        if (dueSymbols.Count == 0 && networkAvailable)
        {
            preemptiveRefreshSymbols = SelectPreemptiveRefreshSymbolsForPass(
                orderedSymbols,
                refreshWindows,
                cachedQuotes,
                nowUtc,
                refreshPollingInterval);
            if (preemptiveRefreshSymbols.Count > 0)
                scheduledDueSymbols = preemptiveRefreshSymbols;
        }

        TraceRuntimeState(
            "QuoteRefreshPlan",
            new KeyValuePair<string, object?>("ordered_symbol_count", orderedSymbols.Count),
            new KeyValuePair<string, object?>("due_symbol_count", dueSymbols.Count),
            new KeyValuePair<string, object?>("scheduled_due_symbol_count", scheduledDueSymbols.Count),
            new KeyValuePair<string, object?>("preemptive_refresh_symbol_count", preemptiveRefreshSymbols.Count),
            new KeyValuePair<string, object?>("cached_quote_count", cachedQuotes.Count),
            new KeyValuePair<string, object?>("network_available", networkAvailable),
            new KeyValuePair<string, object?>("polling_interval_seconds", refreshPollingInterval.TotalSeconds),
            new KeyValuePair<string, object?>("due_symbols", PreviewSymbols(dueSymbols)),
            new KeyValuePair<string, object?>("scheduled_due_symbols", PreviewSymbols(scheduledDueSymbols)),
            new KeyValuePair<string, object?>("preemptive_refresh_symbols", PreviewSymbols(preemptiveRefreshSymbols)));

        if (dueSymbols.Count == 0 && scheduledDueSymbols.Count == 0)
        {
            foreach (string symbol in orderedSymbols)
            {
                if (results.TryGetValue(symbol, out QuoteSnapshot? cached))
                {
                    bool hasUsableValue = cached.Last.HasValue || cached.PreviousClose.HasValue;
                    bool isHardStaleByAge = IsHardStaleByFetchAge(cached, settings, nowUtc);
                    cached.IsStale = !hasUsableValue || isHardStaleByAge;
                }
            }

            string cacheOnlyLabel = networkAvailable
                ? (results.Count > 0 ? "Live Cache" : "Loading live data")
                : (results.Count > 0 ? "Local Cache" : "Waiting for network");

            TraceRuntimeState(
                "QuoteRefreshSkipped",
                new KeyValuePair<string, object?>("reason", "no_symbols_due"),
                new KeyValuePair<string, object?>("provider_label", cacheOnlyLabel),
                new KeyValuePair<string, object?>("result_quote_count", results.Count));
            TraceRuntime($"Quotes served from live cache. Cached={results.Count} RefreshSeed={refreshSeed}");
            return (results, cacheOnlyLabel);
        }

        using HttpClient officialMacroHttpClient = HttpClientFactory.Create(TimeSpan.FromSeconds(Math.Max(3, settings.HttpTimeoutSeconds)));
        IQuoteProvider officialMacroProvider = _createOfficialMacroProvider(officialMacroHttpClient);
        using HttpClient globalMarketHttpClient = HttpClientFactory.Create(TimeSpan.FromSeconds(Math.Max(3, settings.HttpTimeoutSeconds)));
        IQuoteProvider globalMarketProvider = _createGlobalMarketProvider(globalMarketHttpClient);

        IReadOnlyList<ProviderExecutionPlan> providers = BuildQuoteProviders(
            settings,
            finnhubProvider,
            twelveDataProvider,
            tiingoProvider,
            yahooFinanceProvider,
            refreshSeed);
        HashSet<DataSourceKind> configuredBackupKinds = providers
            .Where(provider => provider.Kind != DataSourceKind.YahooFinance)
            .Select(provider => provider.Kind)
            .ToHashSet();

        List<string> remainingSymbols = RotateSymbols(scheduledDueSymbols, refreshSeed)
            .OrderBy(symbol => cachedQuotes.TryGetValue(symbol, out QuoteSnapshot? cached) && HasUsableQuote(cached) ? 1 : 0)
            .ToList();

        List<string> officialMacroSymbols = remainingSymbols
            .Where(IsOfficialMacroSymbol)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (officialMacroSymbols.Count > 0)
        {
            try
            {
                IReadOnlyList<QuoteSnapshot> officialMacroQuotes = await officialMacroProvider.GetQuotesAsync(officialMacroSymbols, cancellationToken);
                if (officialMacroQuotes.Count > 0)
                {
                    liveProvidersUsed.Add("Cboe");
                    TraceRuntimeState(
                        "OfficialMacroQuotesApplied",
                        new KeyValuePair<string, object?>("requested_symbols", PreviewSymbols(officialMacroSymbols)),
                        new KeyValuePair<string, object?>("fetched_symbols", PreviewSymbols(officialMacroQuotes.Select(quote => quote.Symbol))));

                    foreach (QuoteSnapshot quote in officialMacroQuotes)
                    {
                        results[quote.Symbol] = quote;
                        refreshedSymbols.Add(quote.Symbol);
                        remainingSymbols.RemoveAll(symbol => string.Equals(symbol, quote.Symbol, StringComparison.OrdinalIgnoreCase));
                        NoteLatestUpdatedQuote(quote, ref latestUpdatedSymbol, ref latestUpdatedFetchUtc);
                    }
                }
            }
            catch (Exception ex)
            {
                TraceRuntime($"Official macro provider failed for [{string.Join(", ", officialMacroSymbols)}]: {ex.GetType().Name}: {ex.Message}");
                TraceRuntimeState(
                    "OfficialMacroProviderFailed",
                    new KeyValuePair<string, object?>("requested_symbols", PreviewSymbols(officialMacroSymbols)),
                    new KeyValuePair<string, object?>("remaining_symbols", PreviewSymbols(remainingSymbols)));
            }
        }

        List<string> treasurySymbols = remainingSymbols
            .Where(IsTreasuryMacroSymbol)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (treasurySymbols.Count > 0)
        {
            try
            {
                IReadOnlyList<QuoteSnapshot> treasuryQuotes = await treasuryYieldProvider.GetQuotesAsync(treasurySymbols, cancellationToken);
                if (treasuryQuotes.Count > 0)
                {
                    liveProvidersUsed.Add("US Treasury");
                    TraceRuntimeState(
                        "TreasuryMacroQuotesApplied",
                        new KeyValuePair<string, object?>("requested_symbols", PreviewSymbols(treasurySymbols)),
                        new KeyValuePair<string, object?>("fetched_symbols", PreviewSymbols(treasuryQuotes.Select(quote => quote.Symbol))));

                    foreach (QuoteSnapshot quote in treasuryQuotes)
                    {
                        results[quote.Symbol] = quote;
                        refreshedSymbols.Add(quote.Symbol);
                        remainingSymbols.RemoveAll(symbol => string.Equals(symbol, quote.Symbol, StringComparison.OrdinalIgnoreCase));
                        NoteLatestUpdatedQuote(quote, ref latestUpdatedSymbol, ref latestUpdatedFetchUtc);
                    }
                }
            }
            catch (Exception ex)
            {
                TraceRuntime($"US Treasury macro provider failed for [{string.Join(", ", treasurySymbols)}]: {ex.GetType().Name}: {ex.Message}");
                TraceRuntimeState(
                    "TreasuryMacroProviderFailed",
                    new KeyValuePair<string, object?>("requested_symbols", PreviewSymbols(treasurySymbols)),
                    new KeyValuePair<string, object?>("remaining_symbols", PreviewSymbols(remainingSymbols)));
            }
        }

        List<string> stooqGlobalMarketSymbols = remainingSymbols
            .Where(symbol => StooqGlobalMarketQuoteProvider.CanResolve(symbol) && !ShouldPreferYahooWorldIndex(symbol))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (stooqGlobalMarketSymbols.Count > 0)
        {
            try
            {
                IReadOnlyList<QuoteSnapshot> globalMarketQuotes = await globalMarketProvider.GetQuotesAsync(stooqGlobalMarketSymbols, cancellationToken);
                if (globalMarketQuotes.Count > 0)
                {
                    liveProvidersUsed.Add("Stooq");
                    TraceRuntimeState(
                        "GlobalMarketQuotesApplied",
                        new KeyValuePair<string, object?>("provider", "Stooq"),
                        new KeyValuePair<string, object?>("requested_symbols", PreviewSymbols(stooqGlobalMarketSymbols)),
                        new KeyValuePair<string, object?>("fetched_symbols", PreviewSymbols(globalMarketQuotes.Select(quote => quote.Symbol))));

                    foreach (QuoteSnapshot quote in globalMarketQuotes)
                    {
                        results[quote.Symbol] = quote;
                        refreshedSymbols.Add(quote.Symbol);
                        remainingSymbols.RemoveAll(symbol => string.Equals(symbol, quote.Symbol, StringComparison.OrdinalIgnoreCase));
                        NoteLatestUpdatedQuote(quote, ref latestUpdatedSymbol, ref latestUpdatedFetchUtc);
                    }
                }
                else
                {
                    TraceRuntimeState(
                        "GlobalMarketQuotesNoData",
                        new KeyValuePair<string, object?>("provider", "Stooq"),
                        new KeyValuePair<string, object?>("requested_symbols", PreviewSymbols(stooqGlobalMarketSymbols)));
                }
            }
            catch (Exception ex)
            {
                TraceRuntime($"Global market provider failed for [{string.Join(", ", stooqGlobalMarketSymbols)}]: {ex.GetType().Name}: {ex.Message}");
                TraceRuntimeState(
                    "GlobalMarketProviderFailed",
                    new KeyValuePair<string, object?>("provider", "Stooq"),
                    new KeyValuePair<string, object?>("requested_symbols", PreviewSymbols(stooqGlobalMarketSymbols)),
                    new KeyValuePair<string, object?>("remaining_symbols", PreviewSymbols(remainingSymbols)));
            }
        }

        ProviderExecutionPlan? twelveDataPlan = providers.FirstOrDefault(provider => provider.Kind == DataSourceKind.TwelveData);
        if (twelveDataPlan is not null)
        {
            List<string> aliasSymbols = remainingSymbols
                .Where(symbol => ProviderSymbolAliasCatalog.TryGetQuoteAlias(DataSourceKind.TwelveData, symbol, out _))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (aliasSymbols.Count > 0)
            {
                List<string> requestSymbols = RotateSymbols(aliasSymbols, refreshSeed)
                    .Take(Math.Min(TwelveDataAliasRuntimeBatchSymbols, GetMaximumRequestSymbolCount(DataSourceKind.TwelveData)))
                    .ToList();
                int queryCost = GetQueryCost(twelveDataPlan, requestSymbols.Count);
                TimeSpan minimumReuseInterval = GetMinimumProviderReuseInterval(DataSourceKind.TwelveData, requestSymbols);
                TraceRuntimeState(
                    "AliasQuotesPlan",
                    new KeyValuePair<string, object?>("provider", twelveDataPlan.Label),
                    new KeyValuePair<string, object?>("requested_symbols", PreviewSymbols(requestSymbols)),
                    new KeyValuePair<string, object?>("provider_symbols", PreviewSymbols(requestSymbols.Select(symbol => ProviderSymbolAliasCatalog.GetQuoteRequestSymbol(DataSourceKind.TwelveData, symbol)))),
                    new KeyValuePair<string, object?>("query_cost", queryCost),
                    new KeyValuePair<string, object?>("minimum_reuse_seconds", minimumReuseInterval.TotalSeconds));
                if (_providerBudgetLedgerService.TryReserve(
                        twelveDataPlan.Policy,
                        queryCost,
                        minimumReuseInterval,
                        nowUtc))
                {
                    try
                    {
                        IReadOnlyList<QuoteSnapshot> aliasedQuotes = await twelveDataPlan.Provider.GetQuotesAsync(requestSymbols, cancellationToken);
                        if (aliasedQuotes.Count > 0)
                        {
                            liveProvidersUsed.Add(twelveDataPlan.Label);
                            TraceRuntimeState(
                                "AliasQuotesApplied",
                                new KeyValuePair<string, object?>("provider", twelveDataPlan.Label),
                                new KeyValuePair<string, object?>("requested_symbols", PreviewSymbols(requestSymbols)),
                                new KeyValuePair<string, object?>("fetched_symbols", PreviewSymbols(aliasedQuotes.Select(quote => quote.Symbol))));

                            foreach (QuoteSnapshot quote in aliasedQuotes)
                            {
                                results[quote.Symbol] = quote;
                                refreshedSymbols.Add(quote.Symbol);
                                remainingSymbols.RemoveAll(symbol => string.Equals(symbol, quote.Symbol, StringComparison.OrdinalIgnoreCase));
                                NoteLatestUpdatedQuote(quote, ref latestUpdatedSymbol, ref latestUpdatedFetchUtc);
                            }
                        }
                        else
                        {
                            TraceRuntimeState(
                                "AliasQuotesNoData",
                                new KeyValuePair<string, object?>("provider", twelveDataPlan.Label),
                                new KeyValuePair<string, object?>("requested_symbols", PreviewSymbols(requestSymbols)),
                                new KeyValuePair<string, object?>("provider_symbols", PreviewSymbols(requestSymbols.Select(symbol => ProviderSymbolAliasCatalog.GetQuoteRequestSymbol(DataSourceKind.TwelveData, symbol)))));
                        }
                    }
                    catch (Exception ex)
                    {
                        providerHealthService.MarkFailure(ex.Message);
                        TraceRuntime($"Provider {twelveDataPlan.Label} alias prefetch failed for [{string.Join(", ", requestSymbols)}]: {ex.GetType().Name}: {ex.Message}");
                        TraceRuntimeState(
                            "AliasQuotesFailed",
                            new KeyValuePair<string, object?>("provider", twelveDataPlan.Label),
                            new KeyValuePair<string, object?>("requested_symbols", PreviewSymbols(requestSymbols)),
                            new KeyValuePair<string, object?>("provider_symbols", PreviewSymbols(requestSymbols.Select(symbol => ProviderSymbolAliasCatalog.GetQuoteRequestSymbol(DataSourceKind.TwelveData, symbol)))),
                            new KeyValuePair<string, object?>("message", ex.Message));
                    }
                }
                else
                {
                    TraceRuntimeState(
                        "AliasQuotesSkippedByBudget",
                        new KeyValuePair<string, object?>("provider", twelveDataPlan.Label),
                        new KeyValuePair<string, object?>("requested_symbols", PreviewSymbols(requestSymbols)),
                        new KeyValuePair<string, object?>("provider_symbols", PreviewSymbols(requestSymbols.Select(symbol => ProviderSymbolAliasCatalog.GetQuoteRequestSymbol(DataSourceKind.TwelveData, symbol)))),
                        new KeyValuePair<string, object?>("query_cost", queryCost),
                        new KeyValuePair<string, object?>("minimum_reuse_seconds", minimumReuseInterval.TotalSeconds));
                }
            }
        }

        TraceRuntimeState(
            "ProviderOrder",
            new KeyValuePair<string, object?>("providers", providers.Select(provider => provider.Label)),
            new KeyValuePair<string, object?>("remaining_symbols", PreviewSymbols(remainingSymbols)));
        foreach (ProviderExecutionPlan providerPlan in providers)
        {
            if (remainingSymbols.Count == 0)
                break;

            List<string> requestSymbols = TakeRequestSymbols(remainingSymbols, providerPlan, symbolProfiles, refreshSeed, nowUtc);
            if (requestSymbols.Count == 0)
                continue;

            if (providerPlan.Kind == DataSourceKind.YahooFinance &&
                liveProvidersUsed.Count > 0)
            {
                List<string> deferredSymbols = requestSymbols
                    .Where(symbol => ShouldDeferGeneralYahooSymbol(symbol, configuredBackupKinds, symbolProfiles))
                    .ToList();
                if (deferredSymbols.Count == requestSymbols.Count)
                {
                    TraceRuntimeState(
                        "YahooDeferredToBackupProviders",
                        new KeyValuePair<string, object?>("requested_symbols", PreviewSymbols(requestSymbols)),
                        new KeyValuePair<string, object?>("backup_kinds", configuredBackupKinds),
                        new KeyValuePair<string, object?>("live_providers_used", liveProvidersUsed.OrderBy(label => label, StringComparer.OrdinalIgnoreCase)));
                    continue;
                }

                if (deferredSymbols.Count > 0)
                {
                    requestSymbols = requestSymbols
                        .Except(deferredSymbols, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    if (requestSymbols.Count == 0)
                        continue;
                }
            }

            int queryCost = GetQueryCost(providerPlan, requestSymbols.Count);
            TraceRuntime($"Trying provider {providerPlan.Label} for {requestSymbols.Count} symbols [{string.Join(", ", requestSymbols)}] cost={queryCost}");
            TimeSpan minimumReuseInterval = GetMinimumProviderReuseInterval(providerPlan.Kind, requestSymbols);
            if (!_providerBudgetLedgerService.TryReserve(
                    providerPlan.Policy,
                    queryCost,
                    minimumReuseInterval,
                    nowUtc))
            {
                TraceRuntime($"Provider {providerPlan.Label} skipped by budget/cooldown for [{string.Join(", ", requestSymbols)}]");
                TraceRuntimeState(
                    "ProviderSkipped",
                    new KeyValuePair<string, object?>("provider", providerPlan.Label),
                    new KeyValuePair<string, object?>("kind", providerPlan.Kind),
                    new KeyValuePair<string, object?>("query_cost", queryCost),
                    new KeyValuePair<string, object?>("minimum_reuse_seconds", minimumReuseInterval.TotalSeconds),
                    new KeyValuePair<string, object?>("requested_symbols", PreviewSymbols(requestSymbols)),
                    new KeyValuePair<string, object?>("remaining_before_skip", PreviewSymbols(remainingSymbols)));
                continue;
            }

            try
            {
                IReadOnlyList<QuoteSnapshot> fetched = await providerPlan.Provider.GetQuotesAsync(requestSymbols, cancellationToken);
                if (fetched.Count == 0)
                {
                    TraceRuntime($"Provider {providerPlan.Label} returned no quotes for [{string.Join(", ", requestSymbols)}]");
                    continue;
                }

                providerHealthService.MarkSuccess();
                liveProvidersUsed.Add(providerPlan.Label);
                ClearDedicatedYahooCooldowns(fetched.Select(quote => quote.Symbol));
                TraceRuntime($"Provider {providerPlan.Label} returned {fetched.Count} quotes.");
                TraceRuntimeState(
                    "ProviderReturnedQuotes",
                    new KeyValuePair<string, object?>("provider", providerPlan.Label),
                    new KeyValuePair<string, object?>("requested_symbols", PreviewSymbols(requestSymbols)),
                    new KeyValuePair<string, object?>("fetched_symbols", PreviewSymbols(fetched.Select(quote => quote.Symbol))),
                    new KeyValuePair<string, object?>("remaining_before_apply", PreviewSymbols(remainingSymbols)));

                foreach (QuoteSnapshot quote in fetched)
                {
                    results[quote.Symbol] = quote;
                    refreshedSymbols.Add(quote.Symbol);
                    remainingSymbols.RemoveAll(symbol => string.Equals(symbol, quote.Symbol, StringComparison.OrdinalIgnoreCase));
                    NoteLatestUpdatedQuote(quote, ref latestUpdatedSymbol, ref latestUpdatedFetchUtc);
                }

                TraceRuntimeState(
                    "ProviderApplyComplete",
                    new KeyValuePair<string, object?>("provider", providerPlan.Label),
                    new KeyValuePair<string, object?>("refreshed_symbol_count", refreshedSymbols.Count),
                    new KeyValuePair<string, object?>("remaining_symbol_count", remainingSymbols.Count),
                    new KeyValuePair<string, object?>("remaining_symbols", PreviewSymbols(remainingSymbols)));
            }
            catch (Exception ex)
            {
                if (TryGetPartialQuotes(ex, out IReadOnlyList<QuoteSnapshot>? partialQuotes))
                {
                    IReadOnlyList<QuoteSnapshot> appliedPartialQuotes = partialQuotes!;
                    providerHealthService.MarkSuccess();
                    liveProvidersUsed.Add(providerPlan.Label);
                    ClearDedicatedYahooCooldowns(appliedPartialQuotes.Select(quote => quote.Symbol));
                    TraceRuntime($"Provider {providerPlan.Label} yielded {appliedPartialQuotes.Count} partial quotes before rate limiting.");

                    foreach (QuoteSnapshot quote in appliedPartialQuotes)
                    {
                        results[quote.Symbol] = quote;
                        refreshedSymbols.Add(quote.Symbol);
                        remainingSymbols.RemoveAll(symbol => string.Equals(symbol, quote.Symbol, StringComparison.OrdinalIgnoreCase));
                        NoteLatestUpdatedQuote(quote, ref latestUpdatedSymbol, ref latestUpdatedFetchUtc);
                    }

                    TraceRuntimeState(
                        "ProviderPartialQuotesApplied",
                        new KeyValuePair<string, object?>("provider", providerPlan.Label),
                        new KeyValuePair<string, object?>("partial_symbols", PreviewSymbols(appliedPartialQuotes.Select(quote => quote.Symbol))),
                        new KeyValuePair<string, object?>("remaining_symbol_count", remainingSymbols.Count),
                        new KeyValuePair<string, object?>("remaining_symbols", PreviewSymbols(remainingSymbols)));
                }

                providerHealthService.MarkFailure(ex.Message);
                TraceRuntime($"Provider {providerPlan.Label} failed for [{string.Join(", ", requestSymbols)}]: {ex.GetType().Name}: {ex.Message}");
                if (IsRateLimited(ex))
                {
                    if (providerPlan.Kind == DataSourceKind.YahooFinance)
                        ApplyDedicatedYahooRateLimit(requestSymbols, nowUtc);

                    _providerBudgetLedgerService.NoteRateLimit(providerPlan.Kind, GetRateLimitCooldown(providerPlan.Kind, requestSymbols), nowUtc);
                }

                TraceRuntimeState(
                    "ProviderFailed",
                    new KeyValuePair<string, object?>("provider", providerPlan.Label),
                    new KeyValuePair<string, object?>("kind", providerPlan.Kind),
                    new KeyValuePair<string, object?>("requested_symbols", PreviewSymbols(requestSymbols)),
                    new KeyValuePair<string, object?>("is_rate_limited", IsRateLimited(ex)),
                    new KeyValuePair<string, object?>("remaining_symbols", PreviewSymbols(remainingSymbols)));
            }
        }

        foreach (string symbol in orderedSymbols)
        {
            if (results.ContainsKey(symbol))
                continue;

            if (cachedQuotes.TryGetValue(symbol, out QuoteSnapshot? cached))
            {
                QuoteSnapshot staleQuote = CloneQuote(cached);
                bool hasUsableValue = staleQuote.Last.HasValue || staleQuote.PreviousClose.HasValue;
                staleQuote.IsStale = !hasUsableValue || IsHardStaleByFetchAge(staleQuote, settings, nowUtc);
                results[symbol] = staleQuote;
            }
        }

        foreach (string symbol in orderedSymbols)
        {
            if (!results.TryGetValue(symbol, out QuoteSnapshot? quote))
                continue;

            bool hasUsableValue = quote.Last.HasValue || quote.PreviousClose.HasValue;
            bool isHardStaleByAge = IsHardStaleByFetchAge(quote, settings, nowUtc);
            quote.IsStale = !hasUsableValue || isHardStaleByAge;
        }

        if (results.Count > 0 && (liveProvidersUsed.Count > 0 || cachedQuotes.Count != results.Count))
            await quoteCacheService.SaveAsync(results.Values, cancellationToken);

        bool usedCache = orderedSymbols.Any(symbol => results.ContainsKey(symbol) && !refreshedSymbols.Contains(symbol));
        string providerLabel;
        if (liveProvidersUsed.Count > 0)
        {
            providerLabel = string.Join(" + ", liveProvidersUsed.OrderBy(label => label, StringComparer.OrdinalIgnoreCase));
        }
        else if (results.Count > 0)
        {
            providerLabel = "Local Cache";
        }
        else
        {
            providerLabel = networkAvailable ? "Unavailable" : "Waiting for network";
        }

        if (usedCache && liveProvidersUsed.Count > 0)
            providerLabel += " + Cache";
        if (remainingSymbols.Count > 0 && results.Count > 0)
            providerLabel += " (Partial)";
        if (liveProvidersUsed.Count > 0 && !string.IsNullOrWhiteSpace(latestUpdatedSymbol))
            providerLabel += $", {latestUpdatedSymbol} Updated {TimeFormatHelper.ToAgeString(latestUpdatedFetchUtc)}";

        TraceRuntimeState(
            "QuoteResolutionSummary",
            new KeyValuePair<string, object?>("provider_label", providerLabel),
            new KeyValuePair<string, object?>("result_quote_count", results.Count),
            new KeyValuePair<string, object?>("refreshed_symbol_count", refreshedSymbols.Count),
            new KeyValuePair<string, object?>("stale_symbol_count", results.Values.Count(quote => quote.IsStale)),
            new KeyValuePair<string, object?>("stale_symbols", PreviewSymbols(results.Values.Where(quote => quote.IsStale).Select(quote => quote.Symbol))),
            new KeyValuePair<string, object?>("missing_value_symbols", PreviewSymbols(results.Values.Where(quote => !quote.Last.HasValue && !quote.PreviousClose.HasValue).Select(quote => quote.Symbol))),
            new KeyValuePair<string, object?>("remaining_symbol_count", remainingSymbols.Count),
            new KeyValuePair<string, object?>("remaining_symbols", PreviewSymbols(remainingSymbols)),
            new KeyValuePair<string, object?>("macro_missing_symbols", PreviewMissingSymbols(GetMacroIndicatorSymbols(), results, settings)),
            new KeyValuePair<string, object?>("world_index_missing_symbols", PreviewMissingSymbols(FloatingClockBuilder.GetWorldIndexSymbols(), results, settings)));
        TraceRuntime($"Quotes resolved. ProviderLabel={providerLabel} Refreshed={refreshedSymbols.Count} Cached={results.Count - refreshedSymbols.Count} Remaining={remainingSymbols.Count}");
        return (results, providerLabel);
    }

    private static IReadOnlyList<TickerItem> SelectGraphTickers(TickerGroup group, AppSettings settings, int rotationSeed)
    {
        List<TickerItem> enabledTickers = group.Tickers
            .Where(ticker => ticker.Enabled)
            .ToList();

        if (enabledTickers.Count == 0)
            return [];

        int visibleCount = settings.MaxFloatingGraphsPerTape <= 0
            ? enabledTickers.Count
            : Math.Min(enabledTickers.Count, Math.Max(1, settings.MaxFloatingGraphsPerTape));

        if (visibleCount >= enabledTickers.Count)
            return enabledTickers;

        int normalizedSeed = Math.Abs(rotationSeed);
        int startIndex = normalizedSeed % enabledTickers.Count;
        List<TickerItem> selected = [];
        for (int i = 0; i < visibleCount; i++)
            selected.Add(enabledTickers[(startIndex + i) % enabledTickers.Count]);

        return selected;
    }

    private static IReadOnlyList<(TickerGroup Group, TickerItem Ticker)> SelectGraphTickerPairs(AppSettings settings, int graphRotationSeed)
    {
        const int maxSceneGraphCards = 12;
        List<(TickerGroup Group, List<TickerItem> Tickers)> groupSelections = [];
        int groupIndex = 0;
        foreach (TickerGroup group in settings.Groups.Where(group => group.Enabled))
        {
            groupSelections.Add((group, SelectGraphTickers(group, settings, graphRotationSeed + groupIndex).ToList()));
            groupIndex++;
        }

        List<(TickerGroup Group, TickerItem Ticker)> pairs = [];
        int maxTickerCount = groupSelections.Count == 0 ? 0 : groupSelections.Max(selection => selection.Tickers.Count);
        for (int tickerIndex = 0; tickerIndex < maxTickerCount; tickerIndex++)
        {
            foreach ((TickerGroup group, List<TickerItem> tickers) in groupSelections)
            {
                if (tickerIndex >= tickers.Count)
                    continue;

                pairs.Add((group, tickers[tickerIndex]));
            }
        }

        return pairs.Take(maxSceneGraphCards).ToList();
    }

    private static bool HasEnabledHistorySource(
        string symbol,
        AppSettings settings,
        IReadOnlyDictionary<string, SymbolProfile> symbolProfiles)
    {
        foreach (DataSourcePolicySettings policy in settings.DataSources)
        {
            if (!policy.EnableSingleTickerQueries && !policy.EnableBatchTickerQueries)
                continue;

            bool configured = policy.Kind switch
            {
                DataSourceKind.Finnhub => !string.IsNullOrWhiteSpace(settings.FinnhubApiKey),
                DataSourceKind.TwelveData => !string.IsNullOrWhiteSpace(settings.TwelveDataApiKey),
                DataSourceKind.Tiingo => false,
                DataSourceKind.YahooFinance => true,
                _ => false
            };

            if (!configured)
                continue;

            if (DataSourceSymbolEligibility.IsHistoryEligible(policy.Kind, symbol, symbolProfiles))
                return true;
        }

        return false;
    }

    private static List<string> BuildInterleavedPortfolioSymbols(AppSettings settings)
    {
        List<List<string>> groups = settings.Groups
            .Where(group => group.Enabled)
            .Select(group => group.Tickers
                .Where(ticker => ticker.Enabled)
                .Select(ticker => ticker.Symbol)
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
                .ToList())
            .Where(symbols => symbols.Count > 0)
            .ToList();

        if (groups.Count == 0)
            return [];

        List<string> ordered = [];
        int maxTickerCount = groups.Max(group => group.Count);
        for (int tickerIndex = 0; tickerIndex < maxTickerCount; tickerIndex++)
        {
            foreach (List<string> groupSymbols in groups)
            {
                if (tickerIndex < groupSymbols.Count)
                    ordered.Add(groupSymbols[tickerIndex]);
            }
        }

        return ordered;
    }

    public static IReadOnlyList<string> GetMacroIndicatorSymbols()
        => ["^VIX", "GC=F", "US2M", "US10Y", "DX-Y.NYB", "BZ=F"];

    private List<TapeViewModel> BuildTapeViewModels(AppSettings settings, IReadOnlyDictionary<string, QuoteSnapshot> quotes)
    {
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        List<TapeViewModel> tapes = [];
        foreach (TickerGroup group in settings.Groups.Where(group => group.Enabled))
        {
            List<TickerItem> activeTickers = group.Tickers
                .Where(ticker => ticker.Enabled && !string.IsNullOrWhiteSpace(ticker.Symbol))
                .ToList();
            if (activeTickers.Count == 0)
                continue;

            TapeViewModel tape = new()
            {
                Title = string.IsNullOrWhiteSpace(group.Name) ? "Tape" : group.Name.Trim(),
                Speed = group.Speed,
                Direction = group.Direction
            };

            foreach (TickerItem ticker in activeTickers)
            {
                quotes.TryGetValue(ticker.Symbol, out QuoteSnapshot? quote);
                tape.Items.Add(BuildTapeItem(ticker.Symbol, quote, ticker.DisplayName, settings, nowUtc));
            }

            ExpandTapeItems(tape.Items, MinimumTapeItemCount);
            tapes.Add(tape);
        }

        return tapes;
    }

    private static NewsFlasherViewModel BuildNews(IReadOnlyList<string> headlines)
    {
        NewsFlasherViewModel news = new()
        {
            Title = "FINANCE NEWS",
            Speed = Defaults.DefaultNewsSpeed
        };

        foreach (string headline in headlines.Where(headline => !string.IsNullOrWhiteSpace(headline)))
        {
            if (!FinanceNewsService.TryParseSpecialHeadline(headline, out string parsedText, out bool isSupplemental))
                continue;

            news.Headlines.Add(new NewsHeadlineViewModel
            {
                Text = parsedText,
                Foreground = isSupplemental ? Brushes.LightSteelBlue : Brushes.WhiteSmoke,
                IsSupplemental = isSupplemental
            });
        }

        if (news.Headlines.Count == 0)
            news.Headlines.Add(new NewsHeadlineViewModel { Text = "Waiting for summarized financial news..." });

        ExpandHeadlineItems(news.Headlines, MinimumHeadlineCount);
        news.MarqueeText = string.Join(" | ", news.Headlines.Select(headline => headline.Text));
        return news;
    }

    private static TapeItemViewModel BuildTapeItem(string symbol, QuoteSnapshot? quote, string displayName, AppSettings settings, DateTimeOffset nowUtc)
    {
        decimal? last = quote?.Last ?? quote?.PreviousClose;
        decimal? percent = quote?.ChangePercent;
        bool hasUsableValue = last is not null;
        bool isMissing = !hasUsableValue;
        bool isStale = !isMissing && IsQuoteBeyondStaleThreshold(quote, settings, nowUtc);
        string lastText = last is decimal lastValue ? lastValue.ToString("0.00", CultureInfo.InvariantCulture) : string.Empty;
        string percentText = percent is decimal percentValue
            ? $"{(percentValue >= 0 ? "+" : string.Empty)}{percentValue:0.00}%"
            : string.Empty;
        Brush changeBrush = percent switch
        {
            > 0 => Brushes.LimeGreen,
            < 0 => Brushes.OrangeRed,
            _ => Brushes.Gainsboro
        };

        return new TapeItemViewModel
        {
            SymbolText = symbol,
            LastText = lastText,
            ChangeText = percentText,
            IsWaitingOnData = isStale || isMissing,
            HasMissingData = isMissing,
            WaitingGlyphText = isMissing ? "◌" : "🕒",
            WaitingGlyphForeground = isMissing ? Brushes.DarkOrange : Brushes.Goldenrod,
            SymbolForeground = isMissing ? Brushes.DarkOrange : isStale ? Brushes.Gold : changeBrush,
            LastForeground = Brushes.WhiteSmoke,
            ChangeForeground = changeBrush,
            QuoteUpdateToken = quote?.FetchTimestampUtc.UtcTicks ?? 0
        };
    }

    private static bool IsQuoteBeyondStaleThreshold(QuoteSnapshot? quote, AppSettings settings, DateTimeOffset nowUtc)
    {
        if (quote is null)
            return true;

        if (quote.IsStale)
            return true;

        return IsHardStaleByFetchAge(quote, settings, nowUtc);
    }

    // Staleness is based on last successful fetch time, not provider market timestamp.
    private static bool IsHardStaleByFetchAge(QuoteSnapshot quote, AppSettings settings, DateTimeOffset nowUtc)
        => nowUtc - quote.FetchTimestampUtc >= QuoteRefreshPolicy.GetHardStaleThreshold(settings, nowUtc);

    private static bool ShouldShowNetworkWaitingOverlay(bool networkProbeAvailable, string providerLabel)
    {
        if (networkProbeAvailable)
            return false;

        if (string.IsNullOrWhiteSpace(providerLabel))
            return true;

        if (providerLabel.Contains("Waiting for network", StringComparison.OrdinalIgnoreCase))
            return true;

        if (providerLabel.Contains("Unavailable", StringComparison.OrdinalIgnoreCase))
            return true;

        return providerLabel.StartsWith("Local Cache", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldShowLiveRecoveryOverlay(
        IReadOnlyDictionary<string, QuoteSnapshot> quotes,
        AppSettings settings,
        DateTimeOffset nowUtc)
    {
        if (quotes.Count == 0)
            return true;

        int degradedCount = quotes.Values.Count(quote => IsQuoteBeyondStaleThreshold(quote, settings, nowUtc));
        if (degradedCount == 0)
            return false;

        if (degradedCount == quotes.Count)
            return true;

        return degradedCount >= 12 && degradedCount * 2 >= quotes.Count;
    }

    private FloatingGraphViewModel BuildGraph(string tapeName, TickerHistorySnapshot snapshot, AppSettings settings)
    {
        FloatingGraphViewModel graph = _historicalGraphBuilder.Build(tapeName, snapshot, new Size(132, 40));
        graph.Width = 186;
        graph.Height = 78;
        graph.PlotWidth = 132;
        graph.PlotHeight = 40;
        graph.BounceWithinViewport = settings.EnableBouncingGraphCards;
        return graph;
    }

    private static bool SnapshotsEquivalent(TickerHistorySnapshot left, TickerHistorySnapshot right)
    {
        if (left.Points.Count != right.Points.Count)
            return false;

        if (left.Points.Count == 0)
            return true;

        HistoricalPricePoint leftLast = left.Points[^1];
        HistoricalPricePoint rightLast = right.Points[^1];
        return leftLast.TimestampUtc == rightLast.TimestampUtc && leftLast.Close == rightLast.Close;
    }

    private static List<string> RotateSymbols(IEnumerable<string> symbols, int refreshSeed)
    {
        List<string> distinctSymbols = symbols
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (distinctSymbols.Count <= 1)
            return distinctSymbols;

        int normalizedSeed = Math.Abs(refreshSeed) % distinctSymbols.Count;
        if (normalizedSeed == 0)
            return distinctSymbols;

        return
        [
            .. distinctSymbols.Skip(normalizedSeed),
            .. distinctSymbols.Take(normalizedSeed)
        ];
    }

    private static IEnumerable<List<string>> ChunkSymbols(IReadOnlyList<string> symbols, int chunkSize)
    {
        if (chunkSize <= 0)
            yield break;

        for (int index = 0; index < symbols.Count; index += chunkSize)
            yield return symbols.Skip(index).Take(Math.Min(chunkSize, symbols.Count - index)).ToList();
    }

    private static IReadOnlyList<ProviderExecutionPlan> BuildQuoteProviders(
        AppSettings settings,
        IQuoteProvider finnhubProvider,
        IQuoteProvider twelveDataProvider,
        IQuoteProvider tiingoProvider,
        IQuoteProvider yahooFinanceProvider,
        int rotationSeed)
    {
        ProviderExecutionPlan? yahooPrimary = null;
        List<ProviderExecutionPlan> backupProviders = [];

        foreach (DataSourcePolicySettings policy in settings.DataSources)
        {
            DataSourceCapabilities capabilities = DataSourceCatalog.GetCapabilities(policy.Kind);
            if (!policy.EnableSingleTickerQueries && !policy.EnableBatchTickerQueries)
                continue;

            switch (policy.Kind)
            {
                case DataSourceKind.Finnhub when !string.IsNullOrWhiteSpace(settings.FinnhubApiKey):
                    backupProviders.Add(new ProviderExecutionPlan(policy.Kind, capabilities.DisplayName, policy, finnhubProvider));
                    break;
                case DataSourceKind.TwelveData when !string.IsNullOrWhiteSpace(settings.TwelveDataApiKey):
                    backupProviders.Add(new ProviderExecutionPlan(policy.Kind, capabilities.DisplayName, policy, twelveDataProvider));
                    break;
                case DataSourceKind.Tiingo when !string.IsNullOrWhiteSpace(settings.TiingoApiKey):
                    backupProviders.Add(new ProviderExecutionPlan(policy.Kind, capabilities.DisplayName, policy, tiingoProvider));
                    break;
                case DataSourceKind.YahooFinance:
                    yahooPrimary = new ProviderExecutionPlan(policy.Kind, capabilities.DisplayName, policy, yahooFinanceProvider);
                    break;
            }
        }

        // Defensive fallback: even if persisted policies were disabled/corrupted,
        // keep Yahoo warmup alive so startup does not degrade into "no sources".
        if (yahooPrimary is null)
        {
            DataSourcePolicySettings yahooFallback = DataSourceCatalog.CreateDefaultPolicy(DataSourceKind.YahooFinance);
            yahooFallback.EnableSingleTickerQueries = true;
            yahooFallback.EnableBatchTickerQueries = true;
            DataSourceCapabilities yahooCapabilities = DataSourceCatalog.GetCapabilities(DataSourceKind.YahooFinance);
            yahooPrimary = new ProviderExecutionPlan(DataSourceKind.YahooFinance, yahooCapabilities.DisplayName, yahooFallback, yahooFinanceProvider);
        }

        List<ProviderExecutionPlan> ordered = [];
        IReadOnlyList<ProviderExecutionPlan> rotatedBackups = RotateProviders(backupProviders, rotationSeed);
        if (rotatedBackups.Count > 0)
        {
            // Prefer providers with healthier current throughput first, keeping Yahoo
            // available as the last-resort path for unresolved symbols.
            ordered.AddRange(rotatedBackups);
            if (yahooPrimary is not null)
                ordered.Add(yahooPrimary);
        }
        else if (yahooPrimary is not null)
        {
            ordered.Add(yahooPrimary);
        }

        return ordered;
    }

    private static IReadOnlyList<T> RotateProviders<T>(IReadOnlyList<T> providers, int rotationSeed)
    {
        if (providers.Count <= 1)
            return providers;

        int normalizedSeed = Math.Abs(rotationSeed) % providers.Count;
        if (normalizedSeed == 0)
            return providers;

        List<T> rotated = [];
        for (int i = 0; i < providers.Count; i++)
            rotated.Add(providers[(normalizedSeed + i) % providers.Count]);

        return rotated;
    }


    private static double GetRefreshSeconds(AppSettings settings)
        => QuoteRefreshPolicy.GetConfiguredRefreshWindow(settings, DateTimeOffset.UtcNow).TotalSeconds;

    private static Dictionary<string, TimeSpan> BuildRefreshWindows(
        AppSettings settings,
        IReadOnlyList<string> portfolioSymbols,
        IReadOnlyList<string> ancillarySymbols)
    {
        TimeSpan portfolioWindow = QuoteRefreshPolicy.GetEffectiveRefreshWindow(settings, DateTimeOffset.UtcNow);

        Dictionary<string, TimeSpan> windows = new(StringComparer.OrdinalIgnoreCase);
        foreach (string symbol in portfolioSymbols.Where(symbol => !string.IsNullOrWhiteSpace(symbol)))
            windows[symbol] = portfolioWindow;

        foreach (string symbol in ancillarySymbols.Where(symbol => !string.IsNullOrWhiteSpace(symbol)))
            windows.TryAdd(symbol, portfolioWindow);

        return windows;
    }

    private static bool IsRefreshDue(
        string symbol,
        TimeSpan refreshWindow,
        IReadOnlyDictionary<string, QuoteSnapshot> cachedQuotes,
        DateTimeOffset nowUtc)
    {
        if (!cachedQuotes.TryGetValue(symbol, out QuoteSnapshot? cached))
            return true;

        TimeSpan stagger = GetRefreshStaggerOffset(symbol, refreshWindow);
        DateTimeOffset dueAt = cached.FetchTimestampUtc + refreshWindow + stagger;
        return cached.IsStale || nowUtc >= dueAt;
    }

    private static TimeSpan GetRefreshStaggerOffset(string symbol, TimeSpan refreshWindow)
    {
        double windowSeconds = Math.Max(1d, refreshWindow.TotalSeconds);
        double maxOffsetSeconds = Math.Min(15d, Math.Max(0d, windowSeconds - 1d));
        if (maxOffsetSeconds < 1d)
            return TimeSpan.Zero;

        int hash = StringComparer.OrdinalIgnoreCase.GetHashCode(symbol) & int.MaxValue;
        int bucketCount = Math.Max(1, (int)Math.Round(maxOffsetSeconds));
        int bucket = hash % bucketCount;
        return TimeSpan.FromSeconds(bucket);
    }

    private static List<string> SelectDueSymbolsForPass(
        IReadOnlyList<string> orderedSymbols,
        HashSet<string> dueSymbols,
        IReadOnlyDictionary<string, TimeSpan> refreshWindows,
        IReadOnlyDictionary<string, QuoteSnapshot> cachedQuotes,
        DateTimeOffset nowUtc,
        TimeSpan pollingInterval)
    {
        if (dueSymbols.Count == 0)
            return [];

        if (dueSymbols.Count <= MaxBatchSymbolsPerPass)
            return orderedSymbols.Where(dueSymbols.Contains).ToList();

        List<string> alwaysInclude = orderedSymbols
            .Where(symbol => dueSymbols.Contains(symbol) && ShouldAlwaysIncludeDueSymbol(symbol))
            .ToList();
        HashSet<string> alwaysIncludeSet = alwaysInclude.ToHashSet(StringComparer.OrdinalIgnoreCase);

        HashSet<string> budgetedDueSymbols = dueSymbols
            .Where(symbol => !alwaysIncludeSet.Contains(symbol))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (budgetedDueSymbols.Count == 0)
            return alwaysInclude;

        int targetCount = CalculateDueSymbolsPerPass(budgetedDueSymbols, refreshWindows, pollingInterval);
        int degradedCount = orderedSymbols.Count(symbol =>
        {
            if (!budgetedDueSymbols.Contains(symbol))
                return false;

            bool missingQuote = !cachedQuotes.TryGetValue(symbol, out QuoteSnapshot? cached);
            return missingQuote || !HasUsableQuote(cached) || cached!.IsStale;
        });
        if (degradedCount >= 6 && degradedCount * 3 >= budgetedDueSymbols.Count)
            targetCount = Math.Max(targetCount, Math.Min(MaxBatchSymbolsPerPass, degradedCount));

        List<string> selected = orderedSymbols
            .Where(symbol => budgetedDueSymbols.Contains(symbol))
            .Select((symbol, index) =>
            {
                bool missingQuote = !cachedQuotes.TryGetValue(symbol, out QuoteSnapshot? cached);
                bool missingValue = missingQuote || !HasUsableQuote(cached);
                bool stale = !missingQuote && cached!.IsStale;
                TimeSpan refreshWindow = refreshWindows.TryGetValue(symbol, out TimeSpan configuredWindow)
                    ? configuredWindow
                    : pollingInterval;
                double overdueSeconds = missingQuote
                    ? double.MaxValue
                    : Math.Max(0d, (nowUtc - (cached!.FetchTimestampUtc + refreshWindow)).TotalSeconds);
                int urgencyRank = missingValue ? 0 : (stale ? 1 : 2);
                return new
                {
                    Symbol = symbol,
                    Index = index,
                    UrgencyRank = urgencyRank,
                    OverdueSeconds = overdueSeconds
                };
            })
            .OrderBy(item => item.UrgencyRank)
            .ThenByDescending(item => item.OverdueSeconds)
            .ThenBy(item => item.Index)
            .Take(targetCount)
            .Select(item => item.Symbol)
            .ToList();
        selected.InsertRange(0, alwaysInclude);
        return selected;
    }

    private static bool ShouldAlwaysIncludeDueSymbol(string symbol)
        => IsOfficialMacroSymbol(symbol) ||
           IsTreasuryMacroSymbol(symbol) ||
           IsDedicatedYahooSymbol(symbol) ||
           StooqGlobalMarketQuoteProvider.CanResolve(symbol);

    private static int CalculateDueSymbolsPerPass(
        HashSet<string> dueSymbols,
        IReadOnlyDictionary<string, TimeSpan> refreshWindows,
        TimeSpan pollingInterval)
    {
        if (dueSymbols.Count == 0)
            return 0;

        double pollingSeconds = Math.Max(1d, pollingInterval.TotalSeconds);
        double dueShare = dueSymbols.Sum(symbol =>
        {
            TimeSpan refreshWindow = refreshWindows.TryGetValue(symbol, out TimeSpan configuredWindow)
                ? configuredWindow
                : pollingInterval;
            return pollingSeconds / Math.Max(1d, refreshWindow.TotalSeconds);
        });

        int targetCount = Math.Max(1, (int)Math.Ceiling(dueShare));
        return Math.Min(dueSymbols.Count, targetCount);
    }

    private static List<string> SelectPreemptiveRefreshSymbolsForPass(
        IReadOnlyList<string> orderedSymbols,
        IReadOnlyDictionary<string, TimeSpan> refreshWindows,
        IReadOnlyDictionary<string, QuoteSnapshot> cachedQuotes,
        DateTimeOffset nowUtc,
        TimeSpan pollingInterval)
    {
        return orderedSymbols
            .Where(symbol => refreshWindows.ContainsKey(symbol) && cachedQuotes.ContainsKey(symbol))
            .Select((symbol, index) =>
            {
                QuoteSnapshot cached = cachedQuotes[symbol];
                TimeSpan refreshWindow = refreshWindows[symbol];
                TimeSpan leadTime = GetSoftRefreshLeadTime(refreshWindow, pollingInterval);
                DateTimeOffset dueAt = cached.FetchTimestampUtc + refreshWindow + GetRefreshStaggerOffset(symbol, refreshWindow);
                DateTimeOffset preemptiveAt = dueAt - leadTime;
                if (nowUtc < preemptiveAt)
                    return null;

                double windowSeconds = Math.Max(1d, refreshWindow.TotalSeconds);
                double ageRatio = Math.Clamp((nowUtc - cached.FetchTimestampUtc).TotalSeconds / windowSeconds, 0d, 2d);
                return new
                {
                    Symbol = symbol,
                    Index = index,
                    Priority = ShouldAlwaysIncludeDueSymbol(symbol) ? 0 : 1,
                    AgeRatio = ageRatio
                };
            })
            .Where(item => item is not null)
            .OrderBy(item => item!.Priority)
            .ThenByDescending(item => item!.AgeRatio)
            .ThenBy(item => item!.Index)
            .Take(1)
            .Select(item => item!.Symbol)
            .ToList();
    }

    private static TimeSpan GetSoftRefreshLeadTime(TimeSpan refreshWindow, TimeSpan pollingInterval)
    {
        double leadSeconds = Math.Min(
            pollingInterval.TotalSeconds * 1.5d,
            Math.Max(15d, refreshWindow.TotalSeconds * 0.2d));
        leadSeconds = Math.Min(leadSeconds, Math.Max(15d, refreshWindow.TotalSeconds - 1d));
        return TimeSpan.FromSeconds(Math.Max(15d, leadSeconds));
    }

    private static bool HasUsableQuote(QuoteSnapshot? quote)
        => quote is not null &&
           ((quote.Last is decimal last && last > 0) ||
            (quote.PreviousClose is decimal previousClose && previousClose > 0));

    private List<string> TakeRequestSymbols(
        List<string> remainingSymbols,
        ProviderExecutionPlan providerPlan,
        IReadOnlyDictionary<string, SymbolProfile> symbolProfiles,
        int refreshSeed,
        DateTimeOffset nowUtc)
    {
        if (remainingSymbols.Count == 0)
            return [];

        List<string> eligibleSymbols = remainingSymbols
            .Where(symbol => DataSourceSymbolEligibility.IsEligible(providerPlan.Kind, symbol, symbolProfiles))
            .ToList();
        if (eligibleSymbols.Count == 0 && providerPlan.Kind == DataSourceKind.YahooFinance)
        {
            // Recover from stale/restrictive symbol profile caches that can otherwise
            // block all Yahoo requests and leave the scene in "Provider: Unavailable".
            eligibleSymbols = remainingSymbols
                .Where(symbol => DataSourceSymbolEligibility.IsEligible(providerPlan.Kind, symbol))
                .ToList();
        }

        if (eligibleSymbols.Count == 0)
            return [];

        eligibleSymbols = eligibleSymbols.Where(symbol => !IsTreasuryMacroSymbol(symbol)).ToList();
        if (eligibleSymbols.Count == 0)
            return [];

        if (providerPlan.Kind != DataSourceKind.YahooFinance)
            eligibleSymbols = eligibleSymbols.Where(symbol => !IsDedicatedYahooSymbol(symbol)).ToList();

        if (eligibleSymbols.Count == 0)
            return [];

        if (providerPlan.Kind == DataSourceKind.YahooFinance)
        {
            List<string> dedicatedSymbols = eligibleSymbols
                .Where(IsDedicatedYahooSymbol)
                .ToList();
            if (dedicatedSymbols.Count > 0)
            {
                List<string> availableDedicatedSymbols = RotateSymbols(OrderDedicatedYahooSymbols(dedicatedSymbols), refreshSeed)
                    .Where(symbol => !IsDedicatedYahooSymbolCoolingDown(symbol, nowUtc))
                    .ToList();
                if (availableDedicatedSymbols.Count > 0)
                {
                    List<string> selected = availableDedicatedSymbols
                        .Take(Math.Min(YahooDedicatedRuntimeBatchSymbols, dedicatedSymbols.Count))
                        .ToList();
                    string? preferredSymbol = availableDedicatedSymbols.FirstOrDefault(ShouldPreferYahooWorldIndex);
                    int maxCount = Math.Min(
                        YahooDedicatedRuntimeBatchSymbols + (!string.IsNullOrWhiteSpace(preferredSymbol) ? 1 : 0),
                        dedicatedSymbols.Count);

                    if (!string.IsNullOrWhiteSpace(preferredSymbol) &&
                        !selected.Contains(preferredSymbol, StringComparer.OrdinalIgnoreCase) &&
                        selected.Count < maxCount)
                    {
                        selected.Add(preferredSymbol);
                    }

                    return selected;
                }
            }

            return eligibleSymbols
                .Where(symbol => !IsDedicatedYahooSymbol(symbol))
                .Take(Math.Min(YahooGeneralRuntimeBatchSymbols, eligibleSymbols.Count))
                .ToList();
        }

        int count = providerPlan.Policy.EnableBatchTickerQueries && DataSourceCatalog.GetCapabilities(providerPlan.Kind).SupportsBatchTickerQueries
            ? Math.Min(MaxBatchSymbolsPerPass, eligibleSymbols.Count)
            : Math.Min(MaxSequentialSymbolsPerPass, eligibleSymbols.Count);
        count = Math.Min(count, GetMaximumRequestSymbolCount(providerPlan.Kind));

        return eligibleSymbols.Take(count).ToList();
    }

    private static TimeSpan GetMinimumProviderReuseInterval(DataSourceKind kind, IReadOnlyList<string> requestSymbols)
    {
        if (kind == DataSourceKind.TwelveData)
            return TwelveDataReuseInterval;

        if (kind != DataSourceKind.YahooFinance)
            return TimeSpan.FromSeconds(MinimumQuoteProviderReuseSeconds);

        return requestSymbols.Any(IsDedicatedYahooSymbol)
            ? TimeSpan.FromSeconds(YahooDedicatedProviderReuseSeconds)
            : TimeSpan.FromSeconds(YahooGeneralReuseSeconds);
    }

    private static int GetQueryCost(ProviderExecutionPlan providerPlan, int requestedSymbolCount)
    {
        if (requestedSymbolCount <= 0)
            return 0;

        if (providerPlan.Kind == DataSourceKind.TwelveData)
            return TwelveDataPerRequestMinuteOverhead + requestedSymbolCount;

        return providerPlan.Policy.EnableBatchTickerQueries && DataSourceCatalog.GetCapabilities(providerPlan.Kind).SupportsBatchTickerQueries
            ? 1
            : requestedSymbolCount;
    }

    private static int GetMaximumRequestSymbolCount(DataSourceKind kind)
    {
        if (kind != DataSourceKind.TwelveData)
            return int.MaxValue;

        int minuteBudget = DataSourceCatalog.GetCapabilities(kind).HardMaxQueriesPerMinute - TwelveDataMinuteSafetyReserve;
        if (minuteBudget <= TwelveDataPerRequestMinuteOverhead)
            return 1;

        return Math.Max(1, minuteBudget - TwelveDataPerRequestMinuteOverhead);
    }

    private static bool IsRateLimited(Exception ex)
        => ex is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } ||
           ex.Message.Contains("429", StringComparison.OrdinalIgnoreCase) ||
           ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ||
           ex.Message.Contains("API credits", StringComparison.OrdinalIgnoreCase) ||
           ex.Message.Contains("current minute", StringComparison.OrdinalIgnoreCase);

    private static bool TryGetPartialQuotes(Exception ex, out IReadOnlyList<QuoteSnapshot>? partialQuotes)
    {
        if (ex is PartialQuoteResultException partial && partial.PartialQuotes.Count > 0)
        {
            partialQuotes = partial.PartialQuotes;
            return true;
        }

        partialQuotes = null;
        return false;
    }

    private static TimeSpan GetRateLimitCooldown(DataSourceKind kind, IReadOnlyList<string>? requestSymbols = null) => kind switch
    {
        DataSourceKind.Finnhub => TimeSpan.FromMinutes(1),
        DataSourceKind.TwelveData => TimeSpan.FromMinutes(2),
        DataSourceKind.YahooFinance when requestSymbols is not null && requestSymbols.Any(IsDedicatedYahooSymbol) => TimeSpan.FromMinutes(YahooDedicatedRateLimitCooldownMinutes),
        DataSourceKind.YahooFinance => TimeSpan.FromMinutes(YahooGeneralRateLimitCooldownMinutes),
        _ => TimeSpan.FromMinutes(15)
    };

    private static QuoteSnapshot CloneQuote(QuoteSnapshot source)
        => new()
        {
            Symbol = source.Symbol,
            Last = source.Last,
            Change = source.Change,
            ChangePercent = source.ChangePercent,
            PreviousClose = source.PreviousClose,
            Currency = source.Currency,
            MarketSession = source.MarketSession,
            ProviderTimestampUtc = source.ProviderTimestampUtc,
            FetchTimestampUtc = source.FetchTimestampUtc,
            IsStale = source.IsStale
        };

    private static void NoteLatestUpdatedQuote(
        QuoteSnapshot quote,
        ref string? latestUpdatedSymbol,
        ref DateTimeOffset latestUpdatedFetchUtc)
    {
        if (quote.FetchTimestampUtc < latestUpdatedFetchUtc)
            return;

        latestUpdatedFetchUtc = quote.FetchTimestampUtc;
        latestUpdatedSymbol = quote.Symbol;
    }

    private static void ExpandTapeItems(ICollection<TapeItemViewModel> items, int minimumCount)
    {
        if (items.Count == 0 || items.Count >= minimumCount)
            return;

        List<TapeItemViewModel> source = items.ToList();
        int cycleIndex = 0;

        while (items.Count < minimumCount)
        {
            int rotationOffset = CalculateTapeRepeatRotationOffset(source.Count, cycleIndex);
            foreach (TapeItemViewModel item in EnumerateTapeCycle(source, rotationOffset, items.LastOrDefault()?.SymbolText))
            {
                items.Add(CloneTapeItem(item));
                if (items.Count >= minimumCount)
                    break;
            }

            cycleIndex++;
        }
    }

    private static IEnumerable<TapeItemViewModel> EnumerateTapeCycle(
        IReadOnlyList<TapeItemViewModel> source,
        int rotationOffset,
        string? priorSymbol)
    {
        if (source.Count == 0)
            yield break;

        int startIndex = Math.Clamp(rotationOffset, 0, Math.Max(0, source.Count - 1));
        bool firstItem = true;
        for (int index = 0; index < source.Count; index++)
        {
            TapeItemViewModel next = source[(startIndex + index) % source.Count];
            if (firstItem &&
                !string.IsNullOrWhiteSpace(priorSymbol) &&
                source.Count > 1 &&
                string.Equals(next.SymbolText, priorSymbol, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            firstItem = false;
            yield return next;
        }
    }

    private static int CalculateTapeRepeatRotationOffset(int itemCount, int cycleIndex)
    {
        if (itemCount <= 2)
            return cycleIndex % Math.Max(1, itemCount);

        int step = Math.Max(1, itemCount / 2);
        return (cycleIndex * step) % itemCount;
    }

    private static void ExpandHeadlineItems(ICollection<NewsHeadlineViewModel> items, int minimumCount)
    {
        if (items.Count == 0 || items.Count >= minimumCount)
            return;

        List<NewsHeadlineViewModel> source = items
            .Where(item => !item.IsSupplemental)
            .Select(item => new NewsHeadlineViewModel
            {
                Text = item.Text,
                Foreground = item.Foreground,
                IsSupplemental = item.IsSupplemental
            })
            .ToList();

        if (source.Count == 0)
            return;

        while (items.Count < minimumCount)
        {
            foreach (NewsHeadlineViewModel item in source)
            {
                items.Add(new NewsHeadlineViewModel
                {
                    Text = item.Text,
                    Foreground = item.Foreground,
                    IsSupplemental = item.IsSupplemental
                });
                if (items.Count >= minimumCount)
                    break;
            }
        }
    }

    private static TapeItemViewModel CloneTapeItem(TapeItemViewModel item) => new()
    {
        SymbolText = item.SymbolText,
        LastText = item.LastText,
        ChangeText = item.ChangeText,
        IsWaitingOnData = item.IsWaitingOnData,
        HasMissingData = item.HasMissingData,
        WaitingGlyphText = item.WaitingGlyphText,
        WaitingGlyphForeground = item.WaitingGlyphForeground,
        SymbolForeground = item.SymbolForeground,
        LastForeground = item.LastForeground,
        ChangeForeground = item.ChangeForeground
    };

    private static void TraceGraph(string message)
    {
        TraceLog.Info("StartupCoordinator.Graph", message);
    }

    private static void TraceRuntimeState(string eventName, params KeyValuePair<string, object?>[] fields)
    {
        TraceLog.InfoState("StartupCoordinator.Runtime", eventName, fields);
    }

    private static void TraceRuntime(string message)
    {
        TraceLog.Info("StartupCoordinator.Runtime", message);
    }

    private static IReadOnlyList<string> GetDedicatedYahooWarmupSymbols(AppSettings settings)
    {
        IEnumerable<string> symbols = GetYahooDedicatedMacroSymbols();

        return OrderDedicatedYahooSymbols(symbols)
            .Where(RequiresDedicatedYahooWarmup)
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToList();
    }

    private static IReadOnlyList<string> OrderDedicatedYahooSymbols(IEnumerable<string> symbols)
    {
        List<string> orderedSymbols = symbols
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(GetDedicatedYahooPriority)
            .ToList();

        List<string> macroSymbols = orderedSymbols
            .Where(symbol => GetDedicatedYahooBucket(symbol) == 0)
            .ToList();
        List<string> worldIndexSymbols = orderedSymbols
            .Where(symbol => GetDedicatedYahooBucket(symbol) == 1)
            .ToList();
        List<string> remainder = orderedSymbols
            .Where(symbol => GetDedicatedYahooBucket(symbol) > 1)
            .ToList();

        List<string> interleaved = [];
        int pairCount = Math.Max(macroSymbols.Count, worldIndexSymbols.Count);
        for (int i = 0; i < pairCount; i++)
        {
            if (i < macroSymbols.Count)
                interleaved.Add(macroSymbols[i]);

            if (i < worldIndexSymbols.Count)
                interleaved.Add(worldIndexSymbols[i]);
        }

        interleaved.AddRange(remainder);
        return interleaved;
    }

    private static IReadOnlyList<string> PreviewSymbols(IEnumerable<string> symbols, int maxCount = 10)
    {
        List<string> distinct = symbols
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxCount)
            .ToList();

        return distinct;
    }

    private static IReadOnlyList<string> PreviewMissingSymbols(
        IEnumerable<string> expectedSymbols,
        IReadOnlyDictionary<string, QuoteSnapshot> quotes,
        AppSettings settings)
    {
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        return expectedSymbols
            .Where(symbol =>
                !quotes.TryGetValue(symbol, out QuoteSnapshot? quote) ||
                IsQuoteBeyondStaleThreshold(quote, settings, nowUtc) ||
                (quote.Last is null && quote.PreviousClose is null))
            .Take(10)
            .ToList();
    }

    private static string FormatStatusBandText(string statusLine)
        => string.IsNullOrWhiteSpace(statusLine)
            ? "Market (New York): --"
            : statusLine.Replace(" | ", Environment.NewLine, StringComparison.Ordinal);

    private static bool IsDedicatedYahooSymbol(string symbol)
        => DedicatedYahooSymbols.Contains(SymbolProfileHeuristics.Normalize(symbol));

    private static bool IsOfficialMacroSymbol(string symbol)
        => OfficialMacroSymbols.Contains(SymbolProfileHeuristics.Normalize(symbol));

    private static bool IsTreasuryMacroSymbol(string symbol)
        => TreasuryMacroSymbols.Contains(SymbolProfileHeuristics.Normalize(symbol));

    private static bool ShouldDeferGeneralYahooSymbol(
        string symbol,
        IReadOnlyCollection<DataSourceKind> configuredBackupKinds,
        IReadOnlyDictionary<string, SymbolProfile> symbolProfiles)
    {
        if (configuredBackupKinds.Count == 0)
            return false;

        string normalized = SymbolProfileHeuristics.Normalize(symbol);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        if (IsDedicatedYahooSymbol(normalized) || IsOfficialMacroSymbol(normalized) || IsTreasuryMacroSymbol(normalized))
            return false;

        return configuredBackupKinds.Any(kind =>
            DataSourceSymbolEligibility.IsEligible(kind, normalized, symbolProfiles) ||
            DataSourceSymbolEligibility.IsEligible(kind, normalized));
    }

    private static bool RequiresDedicatedYahooWarmup(string symbol)
    {
        string normalized = SymbolProfileHeuristics.Normalize(symbol);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        if (IsOfficialMacroSymbol(normalized) || IsTreasuryMacroSymbol(normalized))
            return false;

        if (ShouldPreferYahooWorldIndex(normalized))
            return true;

        return !StooqGlobalMarketQuoteProvider.CanResolve(normalized);
    }

    private static bool ShouldPreferYahooWorldIndex(string symbol)
        => PreferredYahooWorldIndexSymbols.Contains(SymbolProfileHeuristics.Normalize(symbol));

    private bool IsDedicatedYahooSymbolCoolingDown(string symbol, DateTimeOffset nowUtc)
    {
        string normalized = SymbolProfileHeuristics.Normalize(symbol);
        if (!_dedicatedYahooCooldownsUtc.TryGetValue(normalized, out DateTimeOffset cooldownUntilUtc))
            return false;

        if (cooldownUntilUtc <= nowUtc)
        {
            _dedicatedYahooCooldownsUtc.Remove(normalized);
            return false;
        }

        return true;
    }

    private void ApplyDedicatedYahooRateLimit(IEnumerable<string> symbols, DateTimeOffset nowUtc)
    {
        foreach (string normalized in symbols
                     .Where(IsDedicatedYahooSymbol)
                     .Select(SymbolProfileHeuristics.Normalize)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            _dedicatedYahooCooldownsUtc[normalized] = nowUtc.AddMinutes(YahooDedicatedSymbolCooldownMinutes);
        }
    }

    private void ClearDedicatedYahooCooldowns(IEnumerable<string> symbols)
    {
        foreach (string normalized in symbols
                     .Where(IsDedicatedYahooSymbol)
                     .Select(SymbolProfileHeuristics.Normalize)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            _dedicatedYahooCooldownsUtc.Remove(normalized);
        }
    }

    private static int GetDedicatedYahooPriority(string symbol)
    {
        string normalized = SymbolProfileHeuristics.Normalize(symbol);
        IReadOnlyList<string> macroSymbols = GetYahooDedicatedMacroSymbols();
        int macroIndex = macroSymbols
            .Select((value, index) => (value, index))
            .Where(entry => string.Equals(entry.value, normalized, StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.index)
            .DefaultIfEmpty(-1)
            .First();
        if (macroIndex >= 0)
            return macroIndex;

        IReadOnlyList<string> worldIndexSymbols = FloatingClockBuilder.GetWorldIndexSymbols();
        int worldIndex = worldIndexSymbols
            .Select((value, index) => (value, index))
            .Where(entry => string.Equals(entry.value, normalized, StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.index)
            .DefaultIfEmpty(-1)
            .First();
        return worldIndex >= 0 ? 100 + worldIndex : int.MaxValue;
    }

    private static int GetDedicatedYahooBucket(string symbol)
    {
        int priority = GetDedicatedYahooPriority(symbol);
        if (priority < 100)
            return 0;

        if (priority < int.MaxValue)
            return 1;

        return 2;
    }

    private static HashSet<string> BuildDedicatedYahooSymbolSet()
    {
        HashSet<string> symbols = new(StringComparer.OrdinalIgnoreCase);
        foreach (string symbol in GetYahooDedicatedMacroSymbols())
            symbols.Add(SymbolProfileHeuristics.Normalize(symbol));

        foreach (string symbol in FloatingClockBuilder.GetWorldIndexSymbols())
            symbols.Add(SymbolProfileHeuristics.Normalize(symbol));

        return symbols;
    }

    private static HashSet<string> BuildPreferredYahooWorldIndexSet()
        => new(["^SPX"], StringComparer.OrdinalIgnoreCase);

    private static HashSet<string> BuildOfficialMacroSymbolSet()
        => new(GetOfficialMacroSymbols().Select(SymbolProfileHeuristics.Normalize), StringComparer.OrdinalIgnoreCase);

    private static HashSet<string> BuildTreasuryMacroSymbolSet()
        => new(GetTreasuryMacroSymbols().Select(SymbolProfileHeuristics.Normalize), StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<string> GetYahooDedicatedMacroSymbols()
        => ["DX-Y.NYB"];

    private static IReadOnlyList<string> GetOfficialMacroSymbols()
        => ["^VIX"];

    private static IReadOnlyList<string> GetTreasuryMacroSymbols()
        => ["US2M", "US10Y"];

    private sealed record ProviderExecutionPlan(
        DataSourceKind Kind,
        string Label,
        DataSourcePolicySettings Policy,
        IQuoteProvider Provider);
}

public sealed record StartupWarmupBatch(
    IReadOnlyDictionary<string, QuoteSnapshot> Quotes,
    int CompletedBatches,
    int TotalBatches,
    string StatusMessage);

