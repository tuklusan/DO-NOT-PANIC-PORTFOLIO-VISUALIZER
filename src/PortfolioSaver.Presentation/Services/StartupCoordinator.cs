// ============================================================================
// Copyright (c) 2026 Supratim Sanyal of SANYALnet Labs.
// Proprietary rights reserved except as expressly licensed herein.
//
// DO NOT PANIC PORTFOLIO VISUALIZER
// This file is governed by the SANYALnet Labs Non-Commercial License in the
// root LICENSE file. Non-Commercial use is permitted; Commercial Use and use
// for AI/ML model training are prohibited unless separately authorized.
//
// Attribution is required: "Based on original work by Supratim Sanyal of
// SANYALnet Labs." See LICENSE for full terms, warranty disclaimer, termination,
// patent, trademark, and governing-law provisions.
// ============================================================================
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
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
    private const int MinimumTapeItemCount = 18;
    private const int SequentialQuotePipelineDepth = 4;
    private const int MaxSceneGraphCards = 16;
    private const int MaxGraphBuildCacheEntries = 64;
    private const string StatusFreshnessAnchorSymbol = "^SPX";
    public static readonly TimeSpan LiveQuoteFeedMaximumAge = TimeSpan.FromMinutes(15);

    private readonly ScreensaverSettingsService _settingsService = new();
    private readonly ExchangePhotoCacheService _exchangePhotoCacheService = new();
    private readonly NetworkAvailabilityService _networkAvailabilityService = new();
    private readonly HistoricalGraphBuilder _historicalGraphBuilder = new();
    private readonly FloatingClockBuilder _floatingClockBuilder = new();
    private readonly FinanceNewsService _financeNewsService = new();
    private readonly SymbolProfileStore _symbolProfileStore = new(Path.Combine(PathHelper.GetLocalDataDirectory(), "symbol-profiles.json"));
    private readonly Dictionary<string, QuoteSnapshot> _runtimeQuoteMemory = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _graphBuildCacheGate = new();
    private readonly Dictionary<GraphBuildCacheKey, CachedGraphBuild> _graphBuildCache = [];
    private long _graphBuildCacheSequence;
    private readonly Dictionary<string, PendingQuoteRequest> _pendingQuotePipeline = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _pendingQuotePipelineGate = new();
    private int _sequentialRuntimeCursor;
    private string _sequentialRuntimeFingerprint = string.Empty;
    private readonly Func<bool> _isNetworkAvailable;
    private readonly Func<HttpClient, IQuoteProvider> _createYahooProvider;

    public event Action? BackgroundCacheWarmupCompleted;

    public StartupCoordinator(
        Func<bool>? networkAvailability = null,
        Func<HttpClient, IQuoteProvider>? yahooProviderFactory = null,
        Func<HttpClient, IQuoteProvider>? officialMacroProviderFactory = null,
        Func<HttpClient, IQuoteProvider>? globalMarketProviderFactory = null,
        ProviderBudgetLedgerService? providerBudgetLedgerService = null)
    {
        _isNetworkAvailable = networkAvailability ?? _networkAvailabilityService.IsNetworkAvailable;
        _createYahooProvider = yahooProviderFactory ?? (client => new YahooFinanceQuoteProvider(client, throwOnPartial: false));
        _exchangePhotoCacheService.BackgroundCacheWarmupCompleted += () => BackgroundCacheWarmupCompleted?.Invoke();
    }

    public ScreensaverSceneState BuildBootstrapScene()
    {
        ConsumePendingRuntimeQuoteSeeds();
        AppSettings settings = _settingsService.Load();
        bool networkAvailable = _isNetworkAvailable();
        IReadOnlyList<string> backgroundPaths = _exchangePhotoCacheService.GetImmediateBackgrounds(settings);
        Dictionary<string, QuoteSnapshot> cachedQuotes = _runtimeQuoteMemory.ToDictionary(
            pair => pair.Key,
            pair => CloneQuote(pair.Value),
            StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<string> headlines = _financeNewsService.GetCachedHeadlines(settings.NewsScrollerMode, settings.DeepSeekWritingStyle);
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        bool showNetworkWaitingOverlay = !networkAvailable;

        TraceRuntimeState(
            "BootstrapSceneBuilt",
            new KeyValuePair<string, object?>("network_available", networkAvailable),
            new KeyValuePair<string, object?>("background_count", backgroundPaths.Count),
            new KeyValuePair<string, object?>("cached_quote_count", cachedQuotes.Count),
            new KeyValuePair<string, object?>("headline_count", headlines.Count),
            new KeyValuePair<string, object?>("group_count", settings.Groups.Count(group => group.Enabled)),
            new KeyValuePair<string, object?>("show_network_waiting_overlay", showNetworkWaitingOverlay));

        bool hasBootstrapUpdatedSymbol = TryGetLatestUpdatedSymbol(cachedQuotes, out string latestBootstrapSymbol, out DateTimeOffset latestBootstrapFetchUtc);

        return new ScreensaverSceneState
        {
            Settings = settings,
            Quotes = cachedQuotes,
            Tapes = BuildTapeViewModels(settings, cachedQuotes),
            News = BuildNews(headlines),
            Status = new StatusBarViewModel
            {
                MarketStatusText = "Market (New York): --",
                UpdatedPrefixText = "Last Updated:",
                UpdatedTickerFieldText = FormatUpdatedTickerField(
                    hasBootstrapUpdatedSymbol ? latestBootstrapSymbol : null,
                    hasBootstrapUpdatedSymbol && cachedQuotes.TryGetValue(latestBootstrapSymbol, out QuoteSnapshot? bootstrapQuote)
                        ? bootstrapQuote.ChangePercent
                        : null,
                    hasBootstrapUpdatedSymbol ? latestBootstrapFetchUtc : DateTimeOffset.MinValue),
                UpdatedTickerFieldForeground = ResolveUpdatedTickerFieldBrush(
                    hasBootstrapUpdatedSymbol && cachedQuotes.TryGetValue(latestBootstrapSymbol, out bootstrapQuote)
                        ? bootstrapQuote.ChangePercent
                        : null),
                DataFreshnessText = ResolveDataFreshnessText(networkAvailable, cachedQuotes),
                DataFreshnessForeground = ResolveDataFreshnessBrush(networkAvailable, cachedQuotes),
                ClockDateText = DateTimeOffset.UtcNow.ToString("ddd dd-MMM", CultureInfo.InvariantCulture).ToUpperInvariant(),
                ClockText = $"{DateTimeOffset.UtcNow:HH:mm} UTC"
            },
            Graphs = [],
            Clock = settings.EnableFloatingClock ? _floatingClockBuilder.BuildDefault() : null,
            BackgroundPaths = backgroundPaths,
            BackgroundAttributions = _exchangePhotoCacheService.GetFooterAttributionsForBackgrounds(backgroundPaths),
            ShowNetworkWaitingOverlay = showNetworkWaitingOverlay,
            NetworkWaitingTitle = networkAvailable
                ? "Loading market data"
                : "Waiting for network",
            NetworkWaitingDetail = networkAvailable
                ? "Fetching live quotes, history, and exchange photos..."
                : $"Retrying live quotes and exchange photos every {FormatRefreshCadenceText(settings)}."
        };
    }

    public IReadOnlyList<TapeViewModel> BuildTapesForQuotes(AppSettings settings, IReadOnlyDictionary<string, QuoteSnapshot> quotes)
        => BuildTapeViewModels(settings, quotes);

    public IReadOnlyList<string> BuildOrderedRuntimeSymbols(AppSettings settings)
    {
        List<string> macroSymbols = GetMacroIndicatorSymbols().ToList();
        List<string> worldMarketSymbols = FloatingClockBuilder.GetWorldIndexSymbols().ToList();
        List<string> portfolioSymbols = BuildInterleavedPortfolioSymbols(settings);

        List<string> orderedSymbols =
        [
            .. macroSymbols,
            .. worldMarketSymbols,
            .. portfolioSymbols
        ];

        return orderedSymbols
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }


    public (IReadOnlyList<string> Paths, IReadOnlyDictionary<string, string> Attributions) GetCurrentBackgroundCatalog()
    {
        AppSettings settings = _settingsService.Load();
        IReadOnlyList<string> paths = _exchangePhotoCacheService.GetImmediateBackgrounds(settings);
        return (paths, _exchangePhotoCacheService.GetFooterAttributionsForBackgrounds(paths));
    }
    public async Task<ScreensaverSceneState> BuildSceneAsync(int graphRotationSeed = 0, CancellationToken cancellationToken = default)
    {
        ConsumePendingRuntimeQuoteSeeds();
        AppSettings settings = _settingsService.Load();
        bool networkAvailable = _isNetworkAvailable();
        using HttpClient httpClient = HttpClientFactory.Create(TimeSpan.FromSeconds(Math.Max(3, settings.HttpTimeoutSeconds)));
        IQuoteProvider yahooFinanceProvider = _createYahooProvider(httpClient);

        List<string> portfolioSymbols = BuildInterleavedPortfolioSymbols(settings);
        List<string> ancillarySymbols =
        [
            .. FloatingClockBuilder.GetWorldIndexSymbols(),
            .. GetMacroIndicatorSymbols()
        ];

        Task<Dictionary<string, QuoteSnapshot>> quotesTask = LoadQuotesAsync(
            portfolioSymbols,
            ancillarySymbols,
            settings,
            networkAvailable,
            yahooFinanceProvider,
            graphRotationSeed,
            cancellationToken);
        Task<IReadOnlyList<string>> backgroundsTask = _exchangePhotoCacheService.GetAvailableBackgroundsAsync(
            settings,
            httpClient,
            networkAvailable,
            cancellationToken);
        IReadOnlyList<string> headlines = _financeNewsService.GetCachedHeadlines(settings.NewsScrollerMode, settings.DeepSeekWritingStyle);

        await Task.WhenAll(quotesTask, backgroundsTask);

        Dictionary<string, QuoteSnapshot> quotes = await quotesTask;
        IReadOnlyList<string> backgroundPaths = await backgroundsTask;

        return BuildSceneState(settings, quotes, backgroundPaths, headlines, networkAvailable);
    }

    public async Task<ScreensaverSceneState> BuildProgressiveQuoteSceneAsync(int graphRotationSeed = 0, CancellationToken cancellationToken = default)
    {
        ConsumePendingRuntimeQuoteSeeds();
        AppSettings settings = _settingsService.Load();
        bool networkAvailable = _isNetworkAvailable();
        using HttpClient httpClient = HttpClientFactory.Create(TimeSpan.FromSeconds(Math.Max(3, settings.HttpTimeoutSeconds)));
        IQuoteProvider yahooFinanceProvider = _createYahooProvider(httpClient);

        List<string> portfolioSymbols = BuildInterleavedPortfolioSymbols(settings);
        List<string> ancillarySymbols =
        [
            .. FloatingClockBuilder.GetWorldIndexSymbols(),
            .. GetMacroIndicatorSymbols()
        ];

        Dictionary<string, QuoteSnapshot> quotes = await LoadQuotesAsync(
            portfolioSymbols,
            ancillarySymbols,
            settings,
            networkAvailable,
            yahooFinanceProvider,
            graphRotationSeed,
            cancellationToken);

        IReadOnlyList<string> backgroundPaths = _exchangePhotoCacheService.GetImmediateBackgrounds(settings);
        IReadOnlyList<string> headlines = _financeNewsService.GetCachedHeadlines(settings.NewsScrollerMode, settings.DeepSeekWritingStyle);

        return BuildSceneState(settings, quotes, backgroundPaths, headlines, networkAvailable);
    }

    public void PrimeRuntimeQuotes(IReadOnlyDictionary<string, QuoteSnapshot> quotes)
    {
        foreach ((string symbol, QuoteSnapshot quote) in quotes)
            _runtimeQuoteMemory[symbol] = CloneQuote(quote);
    }

    public async Task<NewsFlasherViewModel> BuildNewsViewModelAsync(
        AppSettings settings,
        bool networkAvailable,
        CancellationToken cancellationToken = default)
    {
        using HttpClient httpClient = HttpClientFactory.Create(FinanceNewsService.GetHttpClientTimeout(settings));
        IReadOnlyList<string> headlines = await _financeNewsService.GetHeadlinesAsync(
            httpClient,
            settings,
            networkAvailable,
            cancellationToken);
        return BuildNews(headlines);
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
        IHistoricalCacheService cacheService = new HistoricalCacheService(settings.HistoricalCacheRootFolder);
        IHistoricalDataProvider historicalProvider = new HybridHistoricalDataProvider(
            cacheService,
            TimeSpan.FromHours(Math.Max(1, settings.HistoricalRefreshHours)));

        List<(TickerGroup Group, TickerItem Ticker)> graphPairs = SelectGraphTickerPairs(settings).ToList();
        Dictionary<string, TickerHistorySnapshot> cachedBySymbol = new(StringComparer.OrdinalIgnoreCase);
        List<string> liveFetchSymbols = [];
        HashSet<string> yieldedSymbols = new(StringComparer.OrdinalIgnoreCase);

        foreach ((TickerGroup group, TickerItem ticker) in graphPairs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TraceGraph($"Graph warmup checking {ticker.Symbol} on {group.Name}.");

            TickerHistorySnapshot? cached = await cacheService.LoadAsync(ticker.Symbol, cancellationToken);
            if (cached is not null && cached.LookbackDays == graphLookbackDays && cached.Points.Count >= 2)
            {
                cachedBySymbol[ticker.Symbol] = cached;
                TraceGraph($"Graph warmup using cache for {ticker.Symbol} with {cached.Points.Count} points.");
                yieldedSymbols.Add(ticker.Symbol);
                yield return BuildGraph(group.Name, cached, settings);
            }

            if (!networkAvailable)
            {
                TraceGraph($"Graph warmup skipped live fetch for {ticker.Symbol} because network is unavailable.");
                continue;
            }

            if (!HasEnabledHistorySource(ticker.Symbol))
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
            yieldedSymbols.Add(ticker.Symbol);
            yield return BuildGraph(group.Name, refreshed, settings);
        }

        foreach ((TickerGroup group, TickerItem ticker) in graphPairs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (yieldedSymbols.Contains(ticker.Symbol))
                continue;

            if (!TryCreateFallbackGraphSnapshot(ticker.Symbol, graphLookbackDays, out TickerHistorySnapshot? fallbackSnapshot) ||
                fallbackSnapshot is null)
                continue;

            TraceGraph($"Graph warmup using quote fallback for {ticker.Symbol}.");
            yield return BuildGraph(group.Name, fallbackSnapshot, settings);
        }
    }

    private async Task<Dictionary<string, QuoteSnapshot>> LoadQuotesAsync(
        IReadOnlyList<string> portfolioSymbols,
        IReadOnlyList<string> benchmarkSymbols,
        AppSettings settings,
        bool networkAvailable,
        IQuoteProvider yahooFinanceProvider,
        int refreshSeed,
        CancellationToken cancellationToken)
    {
        List<string> orderedSymbols =
        [
            .. portfolioSymbols,
            .. benchmarkSymbols
        ];
        orderedSymbols = orderedSymbols
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Dictionary<string, QuoteSnapshot> cachedQuotes = _runtimeQuoteMemory.ToDictionary(
            pair => pair.Key,
            pair => CloneQuote(pair.Value),
            StringComparer.OrdinalIgnoreCase);
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;

        if (!networkAvailable)
            TraceRuntime($"Network probe unavailable. Attempting opportunistic live quote refresh. Cached={cachedQuotes.Count}");

        Dictionary<string, QuoteSnapshot> results = cachedQuotes.ToDictionary(
            pair => pair.Key,
            pair => CloneQuote(pair.Value),
            StringComparer.OrdinalIgnoreCase);
        string? latestUpdatedSymbol = null;
        DateTimeOffset latestUpdatedFetchUtc = DateTimeOffset.MinValue;

        TraceRuntimeState(
            "QuoteRefreshPlan",
            new KeyValuePair<string, object?>("ordered_symbol_count", orderedSymbols.Count),
            new KeyValuePair<string, object?>("cached_quote_count", cachedQuotes.Count),
            new KeyValuePair<string, object?>("network_available", networkAvailable),
            new KeyValuePair<string, object?>("polling_interval_seconds", QuoteRefreshPolicy.GetRefreshPollingInterval(settings, nowUtc).TotalSeconds),
            new KeyValuePair<string, object?>("ordered_symbols", PreviewSymbols(orderedSymbols)));

        if (!networkAvailable || orderedSymbols.Count == 0)
        {
            TraceRuntimeState(
                "QuoteRefreshSkipped",
                new KeyValuePair<string, object?>("reason", networkAvailable ? "no_symbols_configured" : "network_unavailable"),
                new KeyValuePair<string, object?>("result_quote_count", results.Count));
            PrimeRuntimeQuotes(results);
            return results;
        }

        QuotePipelineDrainResult drainResult = await DrainCompletedQuotePipelineAsync(results).ConfigureAwait(false);
        int completedRefreshCount = drainResult.CompletedCount;
        latestUpdatedSymbol = drainResult.LatestUpdatedSymbol;
        latestUpdatedFetchUtc = drainResult.LatestUpdatedFetchUtc;
        QueueQuotePipelineRequests(orderedSymbols, yahooFinanceProvider, cancellationToken);

        foreach (string symbol in orderedSymbols)
        {
            if (results.ContainsKey(symbol))
                continue;

            if (cachedQuotes.TryGetValue(symbol, out QuoteSnapshot? cached))
                results[symbol] = CloneQuote(cached);
        }

        QuotePipelineSnapshot pipelineSnapshot = SnapshotQuotePipeline(orderedSymbols);

        TraceRuntimeState(
            "QuoteResolutionSummary",
            new KeyValuePair<string, object?>("result_quote_count", results.Count),
            new KeyValuePair<string, object?>("refreshed_symbol_count", completedRefreshCount),
            new KeyValuePair<string, object?>("stale_symbol_count", results.Values.Count(quote => quote.IsStale)),
            new KeyValuePair<string, object?>("stale_symbols", PreviewSymbols(results.Values.Where(quote => quote.IsStale).Select(quote => quote.Symbol))),
            new KeyValuePair<string, object?>("missing_value_symbols", PreviewSymbols(results.Values.Where(quote => !quote.Last.HasValue && !quote.PreviousClose.HasValue).Select(quote => quote.Symbol))),
            new KeyValuePair<string, object?>("pipeline_depth", pipelineSnapshot.Depth),
            new KeyValuePair<string, object?>("remaining_symbol_count", pipelineSnapshot.RemainingCount),
            new KeyValuePair<string, object?>("remaining_symbols", PreviewSymbols(pipelineSnapshot.RemainingSymbols)),
            new KeyValuePair<string, object?>("macro_missing_symbols", PreviewMissingSymbols(GetMacroIndicatorSymbols(), results, settings)),
            new KeyValuePair<string, object?>("world_index_missing_symbols", PreviewMissingSymbols(FloatingClockBuilder.GetWorldIndexSymbols(), results, settings)));
        TraceRuntime($"Quotes resolved. Pipeline={pipelineSnapshot.Depth} Cached={results.Count} Remaining={pipelineSnapshot.RemainingCount}");
        PrimeRuntimeQuotes(results);
        return results;
    }

    private sealed record GraphCandidate(
        TickerGroup Group,
        TickerItem Ticker,
        int GroupOrder,
        int TickerOrder,
        decimal Score,
        bool HasLiveMoverScore);

    private sealed record QuotePipelineDrainResult(
        int CompletedCount,
        string? LatestUpdatedSymbol,
        DateTimeOffset LatestUpdatedFetchUtc);

    private sealed record QuotePipelineSnapshot(
        int Depth,
        int RemainingCount,
        IReadOnlyList<string> RemainingSymbols);

    private IReadOnlyList<(TickerGroup Group, TickerItem Ticker)> SelectGraphTickerPairs(AppSettings settings)
    {
        List<GraphCandidate> candidates = settings.Groups
            .Where(group => group.Enabled)
            .SelectMany((group, groupOrder) => group.Tickers
                .Where(ticker => ticker.Enabled && !string.IsNullOrWhiteSpace(ticker.Symbol))
                .Select((ticker, tickerOrder) =>
                {
                    QuoteSnapshot? quote = _runtimeQuoteMemory.TryGetValue(ticker.Symbol, out QuoteSnapshot? cached)
                        ? cached
                        : null;
                    decimal? changePercent = quote?.ChangePercent;
                    return new GraphCandidate(
                        group,
                        ticker,
                        groupOrder,
                        tickerOrder,
                        Math.Abs(changePercent ?? 0m),
                        changePercent.HasValue);
                }))
            .ToList();

        if (candidates.Count == 0)
            return [];

        return candidates
            .OrderByDescending(candidate => candidate.HasLiveMoverScore)
            .ThenByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.GroupOrder)
            .ThenBy(candidate => candidate.TickerOrder)
            .GroupBy(candidate => candidate.Ticker.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(MaxSceneGraphCards)
            .Select(candidate => (candidate.Group, candidate.Ticker))
            .ToList();
    }

    private static bool HasEnabledHistorySource(string symbol)
    {
        return !string.IsNullOrWhiteSpace(SymbolProfileHeuristics.Normalize(symbol));
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
        => ["^VIX", "^IXIC", "^TNX", "^IRX", "GC=F", "BZ=F", "DX-Y.NYB", "BTC-USD"];

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

        return news;
    }

    private static TapeItemViewModel BuildTapeItem(string symbol, QuoteSnapshot? quote, string displayName, AppSettings settings, DateTimeOffset nowUtc)
    {
        decimal? last = quote?.Last ?? quote?.PreviousClose;
        decimal? percent = quote?.ChangePercent;
        bool hasUsableValue = last is not null;
        bool isLoading = quote is null;
        bool isMissing = quote is not null && !hasUsableValue;
        string lastText = last is decimal lastValue
            ? lastValue.ToString("0.00", CultureInfo.InvariantCulture)
            : string.Empty;
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
            IsWaitingOnData = !hasUsableValue,
            HasMissingData = isMissing,
            WaitingGlyphText = isLoading ? "🕒" : isMissing ? "◌" : string.Empty,
            WaitingGlyphForeground = isMissing ? Brushes.DarkOrange : Brushes.Goldenrod,
            SymbolForeground = isMissing ? Brushes.DarkOrange : isLoading ? Brushes.Goldenrod : changeBrush,
            LastForeground = Brushes.WhiteSmoke,
            ChangeForeground = changeBrush,
            QuoteUpdateToken = quote?.FetchTimestampUtc.UtcTicks ?? 0
        };
    }


    private FloatingGraphViewModel BuildGraph(string tapeName, TickerHistorySnapshot snapshot, AppSettings settings)
    {
        const double plotWidth = 132d;
        const double plotHeight = 40d;
        GraphBuildCacheKey cacheKey = new(tapeName, snapshot.Symbol);
        string cacheSignature = BuildGraphCacheSignature(snapshot, plotWidth, plotHeight, settings.EnableBouncingGraphCards);
        lock (_graphBuildCacheGate)
        {
            if (_graphBuildCache.TryGetValue(cacheKey, out CachedGraphBuild? cached) &&
                string.Equals(cached.Signature, cacheSignature, StringComparison.Ordinal))
            {
                _graphBuildCache[cacheKey] = cached with { LastUsed = ++_graphBuildCacheSequence };
                return cached.Graph;
            }
        }

        FloatingGraphViewModel graph = _historicalGraphBuilder.Build(tapeName, snapshot, new Size(132, 40));
        graph.Width = 186;
        graph.Height = 78;
        graph.PlotWidth = 132;
        graph.PlotHeight = 40;
        graph.BounceWithinViewport = settings.EnableBouncingGraphCards;
        lock (_graphBuildCacheGate)
        {
            if (_graphBuildCache.TryGetValue(cacheKey, out CachedGraphBuild? cached) &&
                string.Equals(cached.Signature, cacheSignature, StringComparison.Ordinal))
            {
                _graphBuildCache[cacheKey] = cached with { LastUsed = ++_graphBuildCacheSequence };
                return cached.Graph;
            }

            _graphBuildCache[cacheKey] = new CachedGraphBuild(cacheSignature, graph, ++_graphBuildCacheSequence);
            TrimGraphBuildCacheLocked();
        }

        return graph;
    }

    private void TrimGraphBuildCacheLocked()
    {
        while (_graphBuildCache.Count > MaxGraphBuildCacheEntries)
        {
            GraphBuildCacheKey oldestKey = _graphBuildCache
                .OrderBy(pair => pair.Value.LastUsed)
                .Select(pair => pair.Key)
                .First();
            _graphBuildCache.Remove(oldestKey);
        }
    }

    private static string BuildGraphCacheSignature(TickerHistorySnapshot snapshot, double plotWidth, double plotHeight, bool bounceWithinViewport)
    {
        ArgumentNullException.ThrowIfNull(snapshot.Points);

        StringBuilder builder = new();
        AppendGraphSignatureField(builder, snapshot.Symbol.ToUpperInvariant());
        AppendGraphSignatureField(builder, snapshot.FetchTimestampUtc.UtcTicks.ToString(CultureInfo.InvariantCulture));
        AppendGraphSignatureField(builder, snapshot.LookbackDays.ToString(CultureInfo.InvariantCulture));
        AppendGraphSignatureField(builder, plotWidth.ToString("0.###", CultureInfo.InvariantCulture));
        AppendGraphSignatureField(builder, plotHeight.ToString("0.###", CultureInfo.InvariantCulture));
        AppendGraphSignatureField(builder, bounceWithinViewport ? "1" : "0");
        AppendGraphSignatureField(builder, snapshot.Points.Count.ToString(CultureInfo.InvariantCulture));
        foreach (HistoricalPricePoint point in snapshot.Points)
        {
            AppendGraphSignatureField(builder, point.TimestampUtc.UtcTicks.ToString(CultureInfo.InvariantCulture));
            AppendGraphSignatureField(builder, point.Close.ToString(CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private static void AppendGraphSignatureField(StringBuilder builder, string value)
    {
        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture));
        builder.Append(':');
        builder.Append(value);
    }

    private bool TryCreateFallbackGraphSnapshot(string symbol, int lookbackDays, out TickerHistorySnapshot? snapshot)
    {
        snapshot = null;
        if (!_runtimeQuoteMemory.TryGetValue(symbol, out QuoteSnapshot? quote))
            return false;

        decimal? last = quote.Last ?? quote.PreviousClose;
        if (last is not decimal lastValue)
            return false;

        decimal anchorValue = quote.PreviousClose ?? lastValue;
        DateTimeOffset nowUtc = quote.FetchTimestampUtc == DateTimeOffset.MinValue
            ? DateTimeOffset.UtcNow
            : quote.FetchTimestampUtc;

        snapshot = new TickerHistorySnapshot
        {
            Symbol = symbol,
            FetchTimestampUtc = nowUtc,
            LookbackDays = lookbackDays,
            Points =
            [
                new HistoricalPricePoint
                {
                    TimestampUtc = nowUtc.AddMinutes(-15),
                    Close = anchorValue
                },
                new HistoricalPricePoint
                {
                    TimestampUtc = nowUtc,
                    Close = lastValue
                }
            ]
        };
        return true;
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





    private static string FormatRefreshCadenceText(AppSettings settings)
    {
        TimeSpan cadence = QuoteRefreshPolicy.GetRefreshPollingInterval(settings, DateTimeOffset.UtcNow);
        return cadence < TimeSpan.FromSeconds(1)
            ? $"{cadence.TotalMilliseconds:0} ms"
            : $"{cadence.TotalSeconds:0.##} seconds";
    }

    private static bool HasUsableQuote(QuoteSnapshot? quote)
        => quote is not null &&
           ((quote.Last is decimal last && last > 0) ||
            (quote.PreviousClose is decimal previousClose && previousClose > 0));

    private List<string> TakeSequentialRequestSymbols(IReadOnlyList<string> orderedSymbols, int maxCount, IReadOnlySet<string> inFlightSymbols)
    {
        if (orderedSymbols.Count == 0 || maxCount <= 0)
            return [];

        List<string> sequenceSymbols = orderedSymbols
            .Where(symbol => !string.IsNullOrWhiteSpace(SymbolProfileHeuristics.Normalize(symbol)))
            .ToList();

        if (sequenceSymbols.Count == 0)
            return [];

        string fingerprint = string.Join("|", sequenceSymbols);
        if (!string.Equals(_sequentialRuntimeFingerprint, fingerprint, StringComparison.Ordinal))
        {
            _sequentialRuntimeFingerprint = fingerprint;
            _sequentialRuntimeCursor = 0;
        }

        if (_sequentialRuntimeCursor >= sequenceSymbols.Count)
            _sequentialRuntimeCursor = 0;

        List<string> selected = [];
        int scanned = 0;
        while (selected.Count < maxCount && scanned < sequenceSymbols.Count)
        {
            if (_sequentialRuntimeCursor >= sequenceSymbols.Count)
                _sequentialRuntimeCursor = 0;

            string symbol = sequenceSymbols[_sequentialRuntimeCursor];
            _sequentialRuntimeCursor = (_sequentialRuntimeCursor + 1) % sequenceSymbols.Count;
            scanned++;

            if (inFlightSymbols.Contains(symbol) || selected.Contains(symbol, StringComparer.OrdinalIgnoreCase))
                continue;

            selected.Add(symbol);
        }

        return selected;
    }

    private void QueueQuotePipelineRequests(IReadOnlyList<string> orderedSymbols, IQuoteProvider yahooFinanceProvider, CancellationToken cancellationToken)
    {
        lock (_pendingQuotePipelineGate)
        {
            int capacity = Math.Max(0, SequentialQuotePipelineDepth - _pendingQuotePipeline.Count);
            if (capacity == 0)
                return;

            HashSet<string> inFlightSymbols = _pendingQuotePipeline.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            List<string> requestSymbols = TakeSequentialRequestSymbols(orderedSymbols, capacity, inFlightSymbols);
            foreach (string symbol in requestSymbols)
            {
                if (_pendingQuotePipeline.ContainsKey(symbol))
                    continue;

                string operationId = YFinanceRuntimeClientFactory.CreateOperationId("progressive-quotes");
                TraceRuntimeState(
                    "SequentialQuoteQueued",
                    new KeyValuePair<string, object?>("operation_id", operationId),
                    new KeyValuePair<string, object?>("symbol", symbol),
                    new KeyValuePair<string, object?>("pipeline_depth_before", _pendingQuotePipeline.Count));
                _pendingQuotePipeline[symbol] = new PendingQuoteRequest(
                    symbol,
                    operationId,
                    yahooFinanceProvider.GetQuotesAsync([symbol], cancellationToken),
                    DateTimeOffset.UtcNow);
            }
        }
    }

    private async Task<QuotePipelineDrainResult> DrainCompletedQuotePipelineAsync(
        IDictionary<string, QuoteSnapshot> results)
    {
        List<PendingQuoteRequest> completedRequests = [];
        lock (_pendingQuotePipelineGate)
        {
            List<string> completedSymbols = _pendingQuotePipeline
                .Where(pair => pair.Value.Task.IsCompleted)
                .Select(pair => pair.Key)
                .ToList();

            foreach (string symbol in completedSymbols)
            {
                if (_pendingQuotePipeline.Remove(symbol, out PendingQuoteRequest? pending))
                    completedRequests.Add(pending);
            }
        }

        string? latestUpdatedSymbol = null;
        DateTimeOffset latestUpdatedFetchUtc = DateTimeOffset.MinValue;

        foreach (PendingQuoteRequest pending in completedRequests)
        {
            try
            {
                // The symbol list above only includes completed tasks; await observes exceptions without blocking.
                IReadOnlyList<QuoteSnapshot> fetched = await pending.Task;
                TraceRuntimeState(
                    "SequentialQuoteReturned",
                    new KeyValuePair<string, object?>("operation_id", pending.OperationId),
                    new KeyValuePair<string, object?>("requested_symbols", PreviewSymbols([pending.Symbol])),
                    new KeyValuePair<string, object?>("fetched_symbols", PreviewSymbols(fetched.Select(quote => quote.Symbol))));

                foreach (QuoteSnapshot quote in fetched)
                {
                    results[quote.Symbol] = quote;
                    NoteLatestUpdatedQuote(quote, ref latestUpdatedSymbol, ref latestUpdatedFetchUtc);
                }
            }
            catch (OperationCanceledException ex)
            {
                TraceRuntime($"YFinance.NET pipelined quote cancelled for [{pending.Symbol}]: {ex.GetType().Name}: {ex.Message}");
                TraceRuntimeState(
                    "SequentialQuoteCancelled",
                    new KeyValuePair<string, object?>("operation_id", pending.OperationId),
                    new KeyValuePair<string, object?>("requested_symbols", PreviewSymbols([pending.Symbol])));
            }
            catch (Exception ex)
            {
                if (TryGetPartialQuotes(ex, out IReadOnlyList<QuoteSnapshot>? partialQuotes))
                {
                    foreach (QuoteSnapshot quote in partialQuotes!)
                    {
                        results[quote.Symbol] = quote;
                        NoteLatestUpdatedQuote(quote, ref latestUpdatedSymbol, ref latestUpdatedFetchUtc);
                    }
                }

                TraceRuntime($"YFinance.NET pipelined quote failed for [{pending.Symbol}]: {ex.GetType().Name}: {ex.Message}");
                TraceRuntimeState(
                    "SequentialQuoteFailed",
                    new KeyValuePair<string, object?>("operation_id", pending.OperationId),
                    new KeyValuePair<string, object?>("requested_symbols", PreviewSymbols([pending.Symbol])),
                    new KeyValuePair<string, object?>("is_rate_limited", IsRateLimited(ex)));
            }
        }

        return new QuotePipelineDrainResult(completedRequests.Count, latestUpdatedSymbol, latestUpdatedFetchUtc);
    }

    private QuotePipelineSnapshot SnapshotQuotePipeline(IReadOnlyList<string> orderedSymbols)
    {
        HashSet<string> inFlightSymbols;
        int depth;
        lock (_pendingQuotePipelineGate)
        {
            depth = _pendingQuotePipeline.Count;
            inFlightSymbols = _pendingQuotePipeline.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        List<string> remainingSymbols = orderedSymbols
            .Where(symbol => !inFlightSymbols.Contains(symbol))
            .ToList();
        return new QuotePipelineSnapshot(
            depth,
            Math.Max(0, orderedSymbols.Count - depth),
            remainingSymbols);
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
        return expectedSymbols
            .Where(symbol =>
                !quotes.TryGetValue(symbol, out QuoteSnapshot? quote) ||
                (quote.Last is null && quote.PreviousClose is null))
            .Take(10)
            .ToList();
    }

    private static string FormatStatusBandText(string statusLine)
        => string.IsNullOrWhiteSpace(statusLine)
            ? "Market (New York): --"
            : statusLine.Replace(" | ", Environment.NewLine, StringComparison.Ordinal);




    public static bool TryGetStatusFreshnessAnchorFetchUtc(
        IReadOnlyDictionary<string, QuoteSnapshot> quotes,
        out DateTimeOffset fetchUtc)
    {
        fetchUtc = DateTimeOffset.MinValue;
        if (quotes.Count == 0)
            return false;

        if (quotes.TryGetValue(StatusFreshnessAnchorSymbol, out QuoteSnapshot? spxQuote) &&
            spxQuote.FetchTimestampUtc > DateTimeOffset.MinValue)
        {
            fetchUtc = spxQuote.FetchTimestampUtc;
            return true;
        }

        fetchUtc = quotes.Values
            .Select(quote => quote.FetchTimestampUtc)
            .DefaultIfEmpty(DateTimeOffset.MinValue)
            .Max();

        return fetchUtc > DateTimeOffset.MinValue;
    }

    public static bool TryGetLatestUpdatedSymbol(
        IReadOnlyDictionary<string, QuoteSnapshot> quotes,
        out string symbol,
        out DateTimeOffset fetchUtc)
    {
        symbol = string.Empty;
        fetchUtc = DateTimeOffset.MinValue;
        if (quotes.Count == 0)
            return false;

        (string Symbol, DateTimeOffset FetchUtc) latest = quotes
            .Where(pair => pair.Value.FetchTimestampUtc > DateTimeOffset.MinValue)
            .Select(pair => (Symbol: pair.Key, FetchUtc: pair.Value.FetchTimestampUtc))
            .OrderByDescending(pair => pair.FetchUtc)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(latest.Symbol) || latest.FetchUtc <= DateTimeOffset.MinValue)
            return false;

        symbol = latest.Symbol;
        fetchUtc = latest.FetchUtc;
        return true;
    }










    private ScreensaverSceneState BuildSceneState(
        AppSettings settings,
        IReadOnlyDictionary<string, QuoteSnapshot> quotes,
        IReadOnlyList<string> backgroundPaths,
        IReadOnlyList<string> headlines,
        bool networkAvailable)
    {
        List<TapeViewModel> tapes = BuildTapeViewModels(settings, quotes);
        NewsFlasherViewModel news = BuildNews(headlines);
        FloatingClockViewModel? clock = settings.EnableFloatingClock ? _floatingClockBuilder.BuildDefault() : null;

        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        bool hasLatestUpdatedSymbol = TryGetLatestUpdatedSymbol(quotes, out string latestUpdatedSymbol, out DateTimeOffset latestUpdatedFetchUtc);
        DateTimeOffset lastUpdate = hasLatestUpdatedSymbol
            ? latestUpdatedFetchUtc
            : (TryGetStatusFreshnessAnchorFetchUtc(quotes, out DateTimeOffset anchorQuoteFetchUtc)
                ? anchorQuoteFetchUtc
                : nowUtc);
        StatusBarViewModel status = new()
        {
            MarketStatusText = "Market (New York): --",
            UpdatedPrefixText = "Last Updated:",
            UpdatedTickerFieldText = FormatUpdatedTickerField(hasLatestUpdatedSymbol ? latestUpdatedSymbol : null, hasLatestUpdatedSymbol ? quotes.GetValueOrDefault(latestUpdatedSymbol)?.ChangePercent : null, lastUpdate),
            UpdatedTickerFieldForeground = ResolveUpdatedTickerFieldBrush(hasLatestUpdatedSymbol ? quotes.GetValueOrDefault(latestUpdatedSymbol)?.ChangePercent : null),
            DataFreshnessText = ResolveDataFreshnessText(networkAvailable, quotes),
            DataFreshnessForeground = ResolveDataFreshnessBrush(networkAvailable, quotes),
            ClockDateText = nowUtc.ToString("ddd dd-MMM", CultureInfo.InvariantCulture).ToUpperInvariant(),
            ClockText = $"{nowUtc:HH:mm} UTC"
        };

        bool showNetworkWaitingOverlay = !networkAvailable;

        return new ScreensaverSceneState
        {
            Settings = settings,
            Quotes = new Dictionary<string, QuoteSnapshot>(quotes, StringComparer.OrdinalIgnoreCase),
            Tapes = tapes,
            News = news,
            Status = status,
            Graphs = [],
            Clock = clock,
            BackgroundPaths = backgroundPaths,
            BackgroundAttributions = _exchangePhotoCacheService.GetFooterAttributionsForBackgrounds(backgroundPaths),
            ShowNetworkWaitingOverlay = showNetworkWaitingOverlay,
            NetworkWaitingTitle = "Waiting for network",
            NetworkWaitingDetail = $"Retrying live quotes and exchange photos every {FormatRefreshCadenceText(settings)}."
        };
    }

    private void ConsumePendingRuntimeQuoteSeeds()
    {
        foreach ((string symbol, QuoteSnapshot quote) in RuntimeQuoteSeedStore.ConsumeAll())
            _runtimeQuoteMemory[symbol] = CloneQuote(quote);
    }

    public static string FormatUpdatedTickerField(string? latestSymbol, decimal? changePercent, DateTimeOffset fetchUtc)
    {
        string age = fetchUtc > DateTimeOffset.MinValue
            ? TimeFormatHelper.ToAgeString(fetchUtc)
            : "--";
        string percentText = changePercent is decimal percent
            ? $"{(percent >= 0m ? "+" : string.Empty)}{percent:0.00}%"
            : string.Empty;
        string content = string.IsNullOrWhiteSpace(latestSymbol)
            ? age
            : string.IsNullOrWhiteSpace(percentText)
                ? $"{latestSymbol} {age}"
                : $"{latestSymbol} {percentText} {age}";

        return content.Length >= 25
            ? content[..25]
            : content.PadRight(25);
    }

    public static Brush ResolveUpdatedTickerFieldBrush(decimal? changePercent)
        => changePercent switch
        {
            > 0m => Brushes.LimeGreen,
            < 0m => Brushes.OrangeRed,
            _ => Brushes.Gainsboro
        };

    public static string ResolveDataFreshnessText(bool networkAvailable, IReadOnlyDictionary<string, QuoteSnapshot> quotes)
        => ResolveDataFreshnessText(networkAvailable, quotes, DateTimeOffset.UtcNow);

    public static string ResolveDataFreshnessText(bool networkAvailable, IReadOnlyDictionary<string, QuoteSnapshot> quotes, DateTimeOffset nowUtc)
    {
        if (!networkAvailable)
            return quotes.Count == 0 ? "OFFLINE - waiting for data" : "OFFLINE - showing last values";

        if (quotes.Count == 0)
            return "LOADING - waiting for data";

        return HasStaleQuoteEvidence(quotes, nowUtc)
            ? "STALE - cached values present"
            : "LIVE quote feed";
    }

    public static Brush ResolveDataFreshnessBrush(bool networkAvailable, IReadOnlyDictionary<string, QuoteSnapshot> quotes)
        => ResolveDataFreshnessBrush(networkAvailable, quotes, DateTimeOffset.UtcNow);

    public static Brush ResolveDataFreshnessBrush(bool networkAvailable, IReadOnlyDictionary<string, QuoteSnapshot> quotes, DateTimeOffset nowUtc)
    {
        if (!networkAvailable)
            return Brushes.Orange;

        if (quotes.Count == 0)
            return Brushes.Gainsboro;

        return HasStaleQuoteEvidence(quotes, nowUtc)
            ? Brushes.Goldenrod
            : Brushes.LimeGreen;
    }

    private static bool HasStaleQuoteEvidence(IReadOnlyDictionary<string, QuoteSnapshot> quotes, DateTimeOffset nowUtc)
    {
        if (quotes.Values.Any(quote => quote.IsStale))
            return true;

        // The top-left freshness label describes whether the overall feed is still
        // moving. Per-symbol stale state is rendered on individual ticker widgets.
        DateTimeOffset latestFetchUtc = quotes.Values
            .Where(quote => quote.FetchTimestampUtc > DateTimeOffset.MinValue)
            .Select(quote => quote.FetchTimestampUtc)
            .DefaultIfEmpty(DateTimeOffset.MinValue)
            .Max();

        return latestFetchUtc > DateTimeOffset.MinValue &&
               nowUtc - latestFetchUtc > LiveQuoteFeedMaximumAge;
    }

    public static bool ResolveEffectiveDataFreshnessNetworkState(
        bool networkAvailable,
        int consecutiveQuoteFailures,
        int offlineFailureThreshold)
        => networkAvailable && consecutiveQuoteFailures < Math.Max(1, offlineFailureThreshold);
}

internal sealed record PendingQuoteRequest(
    string Symbol,
    string OperationId,
    Task<IReadOnlyList<QuoteSnapshot>> Task,
    DateTimeOffset StartedUtc);

internal sealed record CachedGraphBuild(
    string Signature,
    FloatingGraphViewModel Graph,
    long LastUsed);

internal readonly record struct GraphBuildCacheKey
{
    public GraphBuildCacheKey(string tapeName, string symbol)
    {
        TapeName = (tapeName ?? string.Empty).ToUpperInvariant();
        Symbol = (symbol ?? string.Empty).ToUpperInvariant();
    }

    public string TapeName { get; }
    public string Symbol { get; }
}
